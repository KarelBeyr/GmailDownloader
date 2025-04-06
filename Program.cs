using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using HtmlAgilityPack;
using MimeKit;
using System.Text.RegularExpressions;

namespace GmailDownloader;

class Program
{
    static readonly string[] Scopes = { GmailService.Scope.GmailReadonly };
    static readonly string ApplicationName = "Gmail API Downloader";

    static void Main(string[] args)
    {
        // 1) OAuth
        UserCredential credential;
        using (var stream = new FileStream("credentials.json", FileMode.Open, FileAccess.Read))
        {
            credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore("token.json", true)
            ).Result;
        }

        // 2) Create Gmail Service
        var service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = ApplicationName
        });

        // 3) List thread IDs
        var listRequest = service.Users.Threads.List("me");
        listRequest.MaxResults = 10;  // for demo
        var listResponse = listRequest.Execute();

        if (listResponse.Threads == null)
        {
            Console.WriteLine("No threads found.");
            return;
        }

        // 4) For each thread, fetch minimal data to get message IDs
        var allThreads = new List<Thread>();

        foreach (var threadItem in listResponse.Threads)
        {
            // a) Get minimal thread to see its messages
            var threadReq = service.Users.Threads.Get("me", threadItem.Id);
            threadReq.Format = UsersResource.ThreadsResource.GetRequest.FormatEnum.Minimal;
            var threadData = threadReq.Execute();

            if (threadData.Messages == null)
                continue;

            // b) Now, fetch each message in raw format and parse with MimeKit
            var customThread = BuildThreadFromMessages(service, threadData.Messages);
            allThreads.Add(customThread);
        }

        Console.WriteLine($"Fetched {allThreads.Count} threads.");

        // 5) Demo output
        if (allThreads.Count > 0)
        {
            var firstThread = allThreads[0];
            Console.WriteLine("First thread subject: " + firstThread.Subject);
            foreach (var email in firstThread.Emails)
            {
                Console.WriteLine($"    From: {email.From}, To: {email.To}, Time: {email.Timestamp}");
                Console.WriteLine($"    Body snippet: {email.Body.Substring(0, Math.Min(email.Body.Length, 100))}...");
            }
        }
    }

    /// <summary>
    /// For each Gmail message reference in the thread, fetch it in RAW format, 
    /// parse with MimeKit, and build our custom Thread/Email objects.
    /// </summary>
    private static Thread BuildThreadFromMessages(
        GmailService service,
        IList<Message> gmailMessages)
    {
        // We'll parse the first message to get the "thread subject"
        var firstMessageId = gmailMessages.First().Id;
        MimeMessage firstParsed = GetRawMimeMessage(service, firstMessageId);
        var threadSubject = firstParsed?.Subject ?? "(No Subject)";

        var customThread = new Thread { Subject = threadSubject };

        // Now parse each message in the thread
        foreach (var gm in gmailMessages)
        {
            var mimeMsg = GetRawMimeMessage(service, gm.Id);
            if (mimeMsg == null)
                continue;

            // Extract From, To, Timestamp
            var fromAddress = mimeMsg.From?.Mailboxes?.FirstOrDefault()?.Address ?? "(Unknown)";
            var toAddress = mimeMsg.To?.Mailboxes?.FirstOrDefault()?.Address ?? "(Unknown)";
            var timestamp = mimeMsg.Date.UtcDateTime;

            // Prefer text body, fallback to stripped HTML
            string textBody = mimeMsg.TextBody;
            if (string.IsNullOrEmpty(textBody) && !string.IsNullOrEmpty(mimeMsg.HtmlBody))
            {
                textBody = StripHtmlTags(mimeMsg.HtmlBody);
            }

            var email = new Email
            {
                From = fromAddress,
                To = toAddress,
                Timestamp = timestamp,
                Body = textBody ?? string.Empty
            };

            customThread.Emails.Add(email);
        }

        return customThread;
    }

    /// <summary>
    /// Calls the Gmail API to fetch a single message in RAW format and loads it as a MimeMessage.
    /// </summary>
    private static MimeMessage GetRawMimeMessage(GmailService service, string messageId)
    {
        try
        {
            var msgReq = service.Users.Messages.Get("me", messageId);
            msgReq.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Raw;
            var rawMsg = msgReq.Execute();

            if (string.IsNullOrEmpty(rawMsg.Raw))
                return null;

            return ConvertRawToMimeMessage(rawMsg.Raw);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving message {messageId}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Decodes Gmail's base64url-encoded raw string into a MimeMessage via MimeKit.
    /// </summary>
    private static MimeMessage ConvertRawToMimeMessage(string rawBase64Url)
    {
        string fixedBase64 = rawBase64Url.Replace('-', '+').Replace('_', '/');
        switch (fixedBase64.Length % 4)
        {
            case 2: fixedBase64 += "=="; break;
            case 3: fixedBase64 += "="; break;
        }
        var rawBytes = Convert.FromBase64String(fixedBase64);

        using (var ms = new MemoryStream(rawBytes))
        {
            return MimeMessage.Load(ms);
        }
    }

    /// <summary>
    /// Quick-and-dirty HTML tag removal using a regex. 
    /// For better fidelity, consider using HtmlAgilityPack.
    /// </summary>
    private static string StripHtmlTags(string html)
    {
        //return System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " ").Trim();
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // doc.DocumentNode.InnerText gives you a naive text extraction,
        // but we can also manually traverse or do additional cleanup if desired.
        var text = doc.DocumentNode.InnerText;

        // Optionally: replace multiple whitespace characters with a single space
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

        return NormalizeSpaces(text.Trim());
    }

    private static string NormalizeSpaces(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Replace non-breaking space (U+00A0) with a normal space
        var output = input.Replace('\u00A0', ' ');

        // Remove zero-width spaces (e.g., U+200B Zero Width Space, U+200C Zero Width Non-Joiner, etc.)
        // The below regex targets several common zero-width characters. Adjust if needed.
        output = Regex.Replace(output, "[\u200B-\u200F\uFEFF]", "");
        output = output.Replace("&zwnj;", "");
        output = output.Replace("&nbsp;", "");

        return output;
    }
}