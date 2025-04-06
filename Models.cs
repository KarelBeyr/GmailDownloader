
namespace GmailDownloader;

public class Thread
{
    public string Subject { get; set; }
    public List<Email> Emails { get; set; } = new List<Email>();
}

public class Email
{
    public string From { get; set; }
    public string To { get; set; }
    public DateTime Timestamp { get; set; }
    public string Body { get; set; }
}