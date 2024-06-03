using System.Diagnostics;
using OpenQA.Selenium;

namespace NotionBackupTool;

class Program
{
    static string GetConfig(string envName, string defaultValue = "")
    {
        string? envVar = Environment.GetEnvironmentVariable(envName);
        
        if(string.IsNullOrEmpty(defaultValue) && string.IsNullOrEmpty(envVar) && string.IsNullOrEmpty(envVar))
        {
            throw new Exception($"Missing configuration env: {envName}");
        }

        return string.IsNullOrEmpty(envVar) ? defaultValue : envVar;
    }
    
    static async Task Main()
    {
        // Wait for debugger
        /*while (!Debugger.IsAttached) // wait here until debugger is attached
        {
            Console.WriteLine("Waiting for debugger..");
            System.Threading.Thread.Sleep(1000); // prevent tight loop
        }*/

        
        
        string mode = GetConfig("MODE");

        string webDriverEndpoint = GetConfig("WEBDRIVER_URL", "http://localhost:4444");

        string s3Host = GetConfig("S3_HOST");
        string s3AccessKey = GetConfig("S3_ACCESS_KEY");
        string s3SecretKey = GetConfig("S3_SECRET_KEY");
        string s3Bucket = GetConfig("S3_BUCKET");

        string notionUser = GetConfig("NOTION_USERNAME");
        string notionPassword = GetConfig("NOTION_PASSWORD");

        string mailHost = GetConfig("IMAP_HOST");
        string mailUser = GetConfig("IMAP_USERNAME");
        string mailPassword = GetConfig("IMAP_PASSWORD");
        string cachePath = GetConfig("CACHE_PATH");

        string temporaryDir = GetConfig("TMP_DIR");
        string gpgPublicKey = GetConfig("GPG_PUBKEY_FILE");

        List<Workspace> workspaces = GetConfig("WORKSPACES").Split(',').Select(w => w.Split(':')).Select(ws => new Workspace { Name = ws[0], Id = ws[1] }).ToList();


        var pingHttp = new HttpClient();

        string hcUrl = GetConfig("HEALTHCHECK_URL");
        await pingHttp.GetAsync($"{hcUrl}/start");

        NotionWebsitePuppeteer grabber = new NotionWebsitePuppeteer(webDriverEndpoint,notionUser, notionPassword)
            {
                MailHost = mailHost,
                MailUser = mailUser,
                MailPassword = mailPassword
            };
        
        if (mode == "download")
        {
            string cookieCachePath = Path.Combine(cachePath, "filecookie.txt");
            string fileCookieValue;

            async Task<Cookie> FileCookie()
            {
                // Grab a session cookie for Notion
                Console.WriteLine("Grabbing Session Cookies...");
                Cookie cookie = grabber.GrabSessionCookies();
                await File.WriteAllTextAsync(cookieCachePath, $"{cookie.Value}|{cookie.Expiry:O}");
                return cookie;
            }

            if (!File.Exists(cookieCachePath))
            {
                Console.WriteLine("No cached cookie present.");
                var fileCookie = await FileCookie();
                fileCookieValue = fileCookie.Value;
            }
            else
            {
                string cookieContent = await File.ReadAllTextAsync(cookieCachePath);
                DateTime expire = DateTime.Parse(cookieContent.Split("|")[1]);
                if (DateTime.Now > expire)
                {
                    Console.WriteLine("Cached cookie expired.");
                    var fileCookie = await FileCookie();
                    fileCookieValue = fileCookie.Value;
                }
                else
                {
                    Console.WriteLine("Using cached cookie.");
                    fileCookieValue = cookieContent.Split("|")[0];
                }
            } 
            
            // Look in Mail inbox for completed exports
            Console.WriteLine("Checking emails...");
            MailGrabber mail = new MailGrabber(mailHost, mailUser, mailPassword);
            List<Tuple<string, string>> downloadUrls = mail.FindUrl("export-noreply@mail.notion.so", @"https://file\.notion\.so/.+\.zip","https://www\\.notion\\.so/space/[a-z0-9]+");
            downloadUrls.AddRange(mail.FindUrl("notify@mail.notion.so", @"https://file\.notion\.so/.+\.zip","https://www\\.notion\\.so/space/[a-z0-9]+"));

            // Download the exports using the session cookies

            // prep cookies
            using HttpClient hc = new HttpClient();

            int ct = 0;
            foreach (Tuple<string, string> urls in downloadUrls)
            {
                // map workspace
                string workspaceId = urls.Item2.Split('/').Last();
                string workspaceName = workspaces.FirstOrDefault(x => x.Id == workspaceId)?.Name ?? String.Empty;

                if (string.IsNullOrEmpty(workspaceName))
                {
                    Console.WriteLine($"Warning: Workspace ID {workspaceId} unknown");
                    workspaceName = workspaceId;
                }
                
                // Start download
                Console.WriteLine($"Processing download #{ct}");
                var req = new HttpRequestMessage(HttpMethod.Get, urls.Item1);
                req.Headers.Add("cookie", $"file_token={fileCookieValue}");

                try
                {
                    var response = await hc.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Unable to initiate download: [{response.StatusCode}] {response.ReasonPhrase}");
                        continue;
                    }

                    string datetime = DateTime.UtcNow.ToString("s").Replace(':', '-');


                    string dlFilename = Path.Combine(temporaryDir, $"notion-{workspaceName}-{datetime}.zip");
                    Console.WriteLine("Downloading...");
                    await using Stream dlStream = await response.Content.ReadAsStreamAsync();
                    await using var fileStream = File.Create(dlFilename);
                    await dlStream.CopyToAsync(fileStream, 1048576);
                    await fileStream.FlushAsync();
                    fileStream.Close();

                    // Encrypt backup
                    FileProcessor fp = new FileProcessor();
                    Console.WriteLine("==> Encrypting...");
                    string encryptedFile = $"{dlFilename}.enc";
                    await fp.EncryptFilePgp(dlFilename, gpgPublicKey, encryptedFile);

                    // Upload to S3
                    Console.WriteLine("==> Send to S3 storage");
                    var s3 = new S3Uploader(s3Host, s3AccessKey, s3SecretKey, s3Bucket);
                    await s3.UploadFileAsync(encryptedFile);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unable to initiate download: {ex.Message}");
                }
                finally
                {
                    ct++;
                }
            }
            
            // Purge Emails
            mail.Purge("export-noreply@mail.notion.so","workspace export");
            mail.Purge("notify@mail.notion.so", "workspace export");

            
        }
        else if (mode == "trigger")
        {
            grabber.TriggerExport(workspaces.Select(x => x.Name).ToList());
        }

        Console.WriteLine("done.");
    }
}