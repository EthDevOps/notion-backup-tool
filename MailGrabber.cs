using System.Text.RegularExpressions;
using MailKit.Net.Imap;
using MimeKit;

namespace NotionBackupTool;

internal class MailGrabber
{
    private readonly string _host;
    private readonly string _user;
    private readonly string _password;

    public MailGrabber(string host, string user, string password)
    {
        _host = host;
        _user = user;
        _password = password;
    }
    
    internal async Task<List<Tuple<string, string>>> FindUrl(string sender, string regexPatternDownload, string regexPatternWorkspace, string subjectFilter = "")
    {
        using var client = new ImapClient();
        client.ServerCertificateValidationCallback = (s, c, h, e) => true;
        client.Connect(_host, 993, true);
        client.Authenticate(_user, _password);
        List<Tuple<string, string>> urls = new List<Tuple<string, string>>();

        var inbox = client.Inbox;
        inbox.Open(MailKit.FolderAccess.ReadOnly);

        for (int i = 0; i < inbox.Count; i++)
        {
            MimeMessage? message = inbox.GetMessage(i);
                
            if((message.From[0] as MailboxAddress)?.Address != sender)
                continue;

            if (!string.IsNullOrEmpty(subjectFilter) && !message.Subject.Contains(subjectFilter))
            {
                continue;
            }
            
            string body = message.HtmlBody;
            

            // Regex to directly extract URLs
            var regexDl = new Regex(regexPatternDownload, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline );
            var regexWorkspace = new Regex(regexPatternWorkspace, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline );

            string dlUrl = String.Empty;
            string workspaceUrl = String.Empty;
            // Report on each match.
            foreach (Match match in regexDl.Matches(body))
            {
                dlUrl = match.Value;
                break;
            }
                
            foreach (Match match in regexWorkspace.Matches(body))
            {
                workspaceUrl = match.Value;
                break;
            }

            if (!string.IsNullOrEmpty(dlUrl))
            {
                urls.Add(new Tuple<string, string>(dlUrl, workspaceUrl));
            }
            
            // Extract mailgun URLs
            // Regex pattern to find all links for the domain mg.mail.notion.so
            string pattern = @"<a[^>]*href=""(?<url>https:\/\/mg\.mail\.notion\.so[^""]+)""[^>]*>[^<]*<\/a>";

            List<string> links = new List<string>();

            // Extract all matching links
            MatchCollection matches = Regex.Matches(body, pattern);
            foreach (Match match in matches)
            {
                links.Add(match.Groups["url"].Value);
            }

            // Perform GET requests and extract the Location header
            // added here
            HttpClientHandler httpClientHandler = new HttpClientHandler();
            httpClientHandler.AllowAutoRedirect = false;
            using HttpClient hc = new HttpClient(httpClientHandler);
            string dlUrl2 = String.Empty;
            string workspaceUrl2 = String.Empty;
            
            foreach (var link in links)
            {
                try
                {
                    // Set the HttpClient to follow redirects
                    hc.DefaultRequestHeaders.Clear();
                    var response = await hc.GetAsync(link);

                    // Check if the response is a redirect
                    if (response.StatusCode == System.Net.HttpStatusCode.MovedPermanently ||
                        response.StatusCode == System.Net.HttpStatusCode.Found ||
                        response.StatusCode == System.Net.HttpStatusCode.SeeOther)
                    {
                        // Extract the Location header
                        if (response.Headers.Location != null)
                        {
                            string actualLink = response.Headers.Location.ToString();
                            if (regexDl.Match(actualLink).Success)
                            {
                                dlUrl2 = actualLink;
                            }
                            if (regexWorkspace.Match(actualLink).Success)
                            {
                                workspaceUrl2 = actualLink;
                            }

                                
                        }
                    }
                    else
                    {
                        Console.WriteLine("No redirect for URL: " + link);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fetching {link}: {ex.Message}");
                }
            }
            if (!string.IsNullOrEmpty(dlUrl2))
            {
                urls.Add(new Tuple<string, string>(dlUrl2, workspaceUrl2));
            }
        }

        await client.DisconnectAsync(true);

        return urls;

    }

    public void Purge(string sender, string subject)
    {
        using var client = new ImapClient();
        client.ServerCertificateValidationCallback = (s, c, h, e) => true;
        client.Connect(_host, 993, true);
        client.Authenticate(_user, _password);

        var inbox = client.Inbox;
        inbox.Open(MailKit.FolderAccess.ReadWrite);
        var purgeFolder = client.GetFolder("notion-backup");
            
        for (int i = 0; i < inbox.Count; i++)
        {
            MimeMessage? message = inbox.GetMessage(i);

            if ((message.From[0] as MailboxAddress)?.Address != sender)
                continue;

            if (!message.Subject.Contains(subject))
                continue;

            inbox.MoveTo(i, purgeFolder);
        }
    }
}