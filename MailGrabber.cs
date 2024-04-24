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
    
    internal List<Tuple<string,string>> FindUrl(string sender, string regexPatternDownload, string regexPatternWorkspace)
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
                
            string body = message.HtmlBody;

            // Regex to extract URLs
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
        }

        client.Disconnect(true);

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