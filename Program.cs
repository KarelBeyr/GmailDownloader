using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using HtmlAgilityPack;
using MimeKit;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using static System.Net.Mime.MediaTypeNames;

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

        // 3) Process each month in the desired range
        int startYear = 2024;
        int endYear = 2025;

        // We'll go up to December of endYear or some other logic:
        for (int year = startYear; year <= endYear; year++)
        {
            for (int month = 1; month <= 12; month++)
            {
                // If you want to stop at a certain date (e.g., current month), you can break out here
                DateTime startOfMonth = new DateTime(year, month, 1);
                DateTime nextMonth = startOfMonth.AddMonths(1);

                // For example, if nextMonth is in the future, we can break
                if (nextMonth > DateTime.Now)
                {
                    // skip or break, depending on your preference
                    break;
                }

                // 3a) Build the Gmail date-based query
                // Gmail needs YYYY/MM/DD. We'll do "after:startOfMonth, before:nextMonth"
                // note that after is inclusive, before is exclusive
                string afterString = startOfMonth.ToString("yyyy/MM/dd");
                string beforeString = nextMonth.ToString("yyyy/MM/dd");
                string query = $"after:{afterString} before:{beforeString}";

                Console.WriteLine($"Processing {year}-{month:00} with query: {query}");

                // 3b) Retrieve threads for this month
                List<Thread> monthlyThreads = FetchThreadsByQuery(service, query);

                // 3c) If we got results, serialize them to threads_YEAR-MONTH.json
                if (monthlyThreads.Any())
                {
                    string fileName = $"threads_{year}-{month:00}.json";
                    StoreThreads(monthlyThreads, fileName);
                    Console.WriteLine($"Wrote {monthlyThreads.Count} threads to {fileName}");
                }
                else
                {
                    Console.WriteLine("No threads found this month.");
                }
            }
        }
    }

    private static void StoreThreads(List<Thread> allThreads, string filename)
    {
        // 1) Configure JSON options (pretty-print, handle all Unicode, etc.)
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            // Encoder that can handle most Unicode characters without escaping them
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        // 2) Serialize to a string
        string json = JsonSerializer.Serialize(allThreads, options);

        // 3) Write to file
        File.WriteAllText(filename, json);
    }

    /// <summary>
    /// Runs a query against the user's mailbox and returns a list of our custom Thread objects.
    /// </summary>
    private static List<Thread> FetchThreadsByQuery(GmailService service, string query)
    {
        var allThreads = new List<Thread>();

        // 1) List thread IDs using a minimal format
        var listRequest = service.Users.Threads.List("me");
        listRequest.Q = query;
        listRequest.MaxResults = 500;  // 500 is max. If this is not sufficient, use NextPageToken somehow
        var listResponse = listRequest.Execute();

        if (listResponse.Threads == null)
            return allThreads; // empty

        Console.WriteLine($"We have {listResponse.Threads.Count()} threads");

        foreach (var threadItem in listResponse.Threads)
        {
            Console.Write(".");
            // 2) Get minimal thread data so we know which messages are in it
            var threadReq = service.Users.Threads.Get("me", threadItem.Id);
            threadReq.Format = UsersResource.ThreadsResource.GetRequest.FormatEnum.Minimal;
            var threadData = threadReq.Execute();

            if (threadData.Messages == null)
                continue;

            // 3) For each message, fetch RAW, parse with MimeKit, build our custom object
            var customThread = BuildThreadFromMessages(service, threadData.Messages, threadData.Id);
            allThreads.Add(customThread);
        }

        return allThreads;
    }

    /// <summary>
    /// For each Gmail message reference in the thread, fetch it in RAW format, 
    /// parse with MimeKit, and build our custom Thread/Email objects.
    /// </summary>
    private static Thread BuildThreadFromMessages(
        GmailService service,
        IList<Message> gmailMessages,
        string threadId)
    {
        // We'll parse the first message to get the "thread subject"
        var firstMessageId = gmailMessages.First().Id;
        MimeMessage firstParsed = GetRawMimeMessage(service, firstMessageId);
        var threadSubject = firstParsed?.Subject ?? "(No Subject)";

        var customThread = new Thread { Subject = threadSubject, ThreadId = threadId };

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

            // odfiltruje email na ktery se odpovida
            var bodyWithoutReply = EmailReplyParser.EmailReplyParser.ParseReply(textBody);

            // odfiltruje radek "Dne čt 3. 4. 2025 8:48 uživatel Ilona Šulová <sulovai@pokrok.cz> napsal:"
            string pattern = @"^.*Dne.*uživatel.*napsal.*$";
            string result = Regex.Replace(bodyWithoutReply, pattern, "", RegexOptions.Multiline);

            // odfiltruje prazdne radky
            result = Regex.Replace(result, @"^\s+$[\r\n]*", "", RegexOptions.Multiline);

            var email = new Email
            {
                From = fromAddress,
                To = toAddress,
                Timestamp = timestamp,
                Body = result ?? string.Empty
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