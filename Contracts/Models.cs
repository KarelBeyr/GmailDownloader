namespace GmailDownloader.Contracts;

public class MailThread
{
    public string Subject { get; set; }
    public List<Email> Emails { get; set; } = new List<Email>();
    public string ThreadId { get; set; }
}

public class Email
{
    public string From { get; set; }
    public string To { get; set; }
    public DateTime Timestamp { get; set; }
    public string Body { get; set; }
}

public class EmbeddedEmail
{
    public string ThreadId { get; set; }
    public string VectorBase64 { get; set; }
}