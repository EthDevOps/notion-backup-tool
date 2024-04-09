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
    
    internal List<string> FindUrl(string sender, string regexPattern)
    {
        List<string> urls = new List<string>();
        using (var client = new ImapClient())
        {
            client.ServerCertificateValidationCallback = (s, c, h, e) => true;
            client.Connect(_host, 993, true);
            client.Authenticate(_user, _password);

            var inbox = client.Inbox;
            inbox.Open(MailKit.FolderAccess.ReadOnly);

            for (int i = 0; i < inbox.Count; i++)
            {
                MimeMessage? message = inbox.GetMessage(i);
                
                if((message.From[0] as MailboxAddress)?.Address != sender)
                    continue;
                
                string body = message.HtmlBody;

                // Regex to extract URLs
                var regex = new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline );

                // Find matches
                MatchCollection matches = regex.Matches(body);

                // Report on each match.
                foreach (Match match in matches)
                {
                    urls.Add(match.Value);
                }
            }

            client.Disconnect(true);
        }

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