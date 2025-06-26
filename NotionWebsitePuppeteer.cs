using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Remote;
using OpenQA.Selenium.Support.UI;

namespace NotionBackupTool;

internal class NotionWebsitePuppeteer
{
    private readonly string _seleniumHost;
    private IWebDriver _driver;
    private readonly string _username;
    private readonly string _password;
    private readonly bool _debugMode;

    public string MailHost { get; set; }
    public string MailUser { get; set; }
    public string MailPassword { get; set; }
    public NotionWebsitePuppeteer(string seleniumHost, string notionUsername, string notionPassword, bool debugMode = false)
    {
        _seleniumHost = seleniumHost;
        _username = notionUsername;
        _password = notionPassword;
        _debugMode = debugMode;
    }

    private void Login()
    {
        void TryLoginWithLink()
        {
            Console.WriteLine("Trying login-code login...");
            // Query mail for login code

            MailGrabber mg = new MailGrabber(MailHost, MailUser, MailPassword);
            string loginUrl = "";
            bool foundUrl = false;

            while (!foundUrl)
            {
                Thread.Sleep(2000);
                Console.WriteLine("Waiting for login code email...");
                List<Tuple<string, string>> loginUrls = mg.FindUrl("notify@mail.notion.so",
                    "https://www\\.notion\\.so/loginwithemail.*?(?=\")", String.Empty).Result;
                
                if (loginUrls.Count == 0) continue;
                
                loginUrl = loginUrls.First().Item1;
                foundUrl = true;
                mg.Purge("notify@mail.notion.so","login code");

            }

            Console.WriteLine("Logging in using URL in 30sec...");
            Thread.Sleep(30000);
            string cleanedUrl = loginUrl.Replace("https://","https").Replace("&amp;", "&").Replace(":", "%3A").Replace("https","https://");
            _driver.Navigate().GoToUrl(cleanedUrl);
            Thread.Sleep(5000);
        }

        bool FirstLoginStep()
        {
            bool needsLoginCode = false;
            Console.WriteLine("\tNavigating to login page...");
            // Navigate to Notes GitHub auth
            _driver.Navigate().GoToUrl($"https://notion.so/login");

            Console.WriteLine("\tSleep 2.5sec");
            Thread.Sleep(2500);
            // Login to GitHub
            Console.WriteLine("\tEntering credentials...");
            IWebElement loginInput = _driver.FindElement(By.Id("notion-email-input-1"));
            loginInput.SendKeys(_username);
        
            IWebElement nextBtn = _driver.FindElement(By.XPath("//form//div[contains(text(), 'Continue')]"));
            nextBtn.Click();

            Console.WriteLine("\tSleep 10sec");
            Thread.Sleep(10000);
            
            // Check if we need to use a login code
            try
            {
                IWebElement loginCodeBtn =
                    _driver.FindElement(By.XPath("//form//label[contains(text(), 'Verification code')]"));
                if (loginCodeBtn != null)
                {
                    needsLoginCode = true;
                }
            }
            catch
            {
                Console.WriteLine("Not in explicit login code mode");
            }

            return needsLoginCode;

        }

        // Instantiate a ChromeDriver
        var options = new ChromeOptions();
        options.AddArgument("--headless=new");

        if (_debugMode)
        {
            _driver = new ChromeDriver();
        }
        else
        {
            _driver = new RemoteWebDriver(new Uri(_seleniumHost), options);
        }
        WebDriverWait wait = new WebDriverWait(_driver, TimeSpan.FromSeconds(120));

        bool needsLoginCode = FirstLoginStep();

        try
        {
            IWebElement loginError =
                _driver.FindElement(By.XPath("//div[contains(text(), 'You must login with an email login code')]"));
            if (loginError != null)
            {
                // Need to reload site and restart to enter login code mode
                Console.WriteLine("Need to restart login.");
                Thread.Sleep(3000);
                needsLoginCode = FirstLoginStep();
            }
        }
        catch
        {
            Console.WriteLine("No login code error. continue.");
        }

        if (needsLoginCode)
        {
            TryLoginWithLink();
        }
        else
        {
            Console.WriteLine("Trying password login...");
            try
            {
                IWebElement passInput = _driver.FindElement(By.Id("notion-password-input-2"));
                passInput.SendKeys(_password);

                Console.WriteLine("\tSending login...");
                IWebElement loginBtn = _driver.FindElement(By.XPath("//form//div[contains(text(), 'Continue with password')]"));
                loginBtn.Click();
            }
            catch
            {
                Console.WriteLine("bad login");
                TryLoginWithLink();
            }
            
        }
        
    }
    
    public void TriggerExport(List<string> workspaceSlug)
    {
        Console.WriteLine("Login...");
        Login();

        foreach (string ws in workspaceSlug)
        {
            try
            {
                Thread.Sleep(10000);
                Console.WriteLine($"Navigate to workspace {ws}...");
                _driver.Navigate().GoToUrl($"https://notion.so/{ws}");

                Thread.Sleep(5000);
                Console.WriteLine("Navigating to Export button...");
                IWebElement nextBtn = _driver.FindElement(By.XPath("//div[@class='notion-sidebar']//div[contains(text(), 'Settings')]"));
                nextBtn.Click();

                Thread.Sleep(3000);
                _driver.FindElement(By.XPath("//div[@class='notion-space-settings']//div[text() = 'General']")).Click();

                Thread.Sleep(3000);
                _driver.FindElement(By.XPath("//div[@class='notion-space-settings']//div[text() = 'Export all workspace content']")).Click();

                Thread.Sleep(3000);
                Console.WriteLine("Trigger Export...");
                _driver.FindElement(By.XPath("//div[@class='notion-dialog']//div[text() = 'Export']")).Click();

                Console.WriteLine($"Export running for {ws}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error triggering export for workspace {ws}: {ex.Message}");
            }
        }
      
        // sleeping to let export init
        Console.WriteLine("Waiting for export to settle...");
        Thread.Sleep(15000);
        _driver.Close();
    }
    
    public Cookie GrabSessionCookies()
    {
        Login();
        //wait.Until(wd => wd.FindElement(By.Id("notion-app")));
        Console.WriteLine("\tSleep 15sec");
        Thread.Sleep(15000);
        Console.WriteLine("Navigate to file URL...");
        _driver.Navigate().GoToUrl($"https://file.notion.so/f/");
        
        Console.WriteLine("\tSleep 5sec");
        Thread.Sleep(5000);
        
        // Wait for return to HackMD
        
        Console.WriteLine("\tGrabbing delicious Cookies...");
        // grab cookies

        var cookie = _driver.Manage().Cookies.GetCookieNamed("file_token");

        if (cookie == null)
        {
            Console.WriteLine("Unable to grab cookie. Trying again...");
            Console.WriteLine("\tSleep 5sec");
            Thread.Sleep(5000);
            cookie = _driver.Manage().Cookies.GetCookieNamed("file_token");
        }
        

        // Close the driver
        _driver.Quit();

        return cookie;
    }
}