using Contracts;
using GmailDownloader.Contracts;
using System.Text;
using System.Text.Json;

public class Program
{
    private static readonly HttpClient httpClient = new HttpClient();

    public static async Task Main(string[] args)
    {
        var inputFiles = Directory.GetFiles(@"c:\Temp\svj\");

        foreach (var inputFile in inputFiles)
        {
            string outputFile = TransformFilename(inputFile);
            Console.WriteLine($"\nProcessing input file {inputFile} into {outputFile}");

            string inputJson = await File.ReadAllTextAsync(inputFile);
            List<MailThread> threads;
            try
            {
                threads = JsonSerializer.Deserialize<List<MailThread>>(inputJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing JSON: {ex.Message}");
                return;
            }

            if (threads == null)
            {
                Console.WriteLine("Error: No threads found in input JSON.");
                return;
            }

            // 3) For each thread, embed the first email's Body
            var embeddedEmails = new List<EmbeddedEmail>();

            foreach (var thread in threads)
            {
                Console.Write(".");
                if (thread.Emails == null || thread.Emails.Count == 0)
                {
                    // No emails in this thread; skip
                    continue;
                }

                // Get the first email's body
                var firstEmail = thread.Emails[0];
                string textToEmbed = firstEmail.Body ?? "";

                // Call the Python embedder to get the vector
                List<float> embeddingVector = await GetEmbeddingAsync(textToEmbed);

                // Store the result
                embeddedEmails.Add(new EmbeddedEmail
                {
                    ThreadId = thread.ThreadId,
                    VectorBase64 = FloatBase64Helper.ConvertFloatListToBase64(embeddingVector)
                });
            }

            // 4) Write the collection of EmbeddedEmail to output JSON file
            string outputJson = JsonSerializer.Serialize(embeddedEmails, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outputFile, outputJson);
        }

        Console.WriteLine($"Done");
    }

    static string TransformFilename(string inputFile)
    {
        // Extract directory, base name, and extension
        string directory = Path.GetDirectoryName(inputFile);
        string baseName = Path.GetFileNameWithoutExtension(inputFile);
        string extension = Path.GetExtension(inputFile);

        // Combine them back, adding "_res" before extension
        string outputFile = Path.Combine(directory, baseName + "_emb" + extension);
        return outputFile;
    }

    // Helper method: send text to Python server, parse the embedding
    private static async Task<List<float>> GetEmbeddingAsync(string text)
    {
        // Our server expects a JSON payload like: { "text": "some text" }
        var requestBody = new { text = text };

        // Create a StringContent with the request JSON, content-type = application/json
        string jsonPayload = JsonSerializer.Serialize(requestBody);
        var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        // Adjust URL if your Python service is on a different port/host
        string embedUrl = "http://localhost:8000/";

        // Make the POST request
        using var response = await httpClient.PostAsync(embedUrl, httpContent);
        response.EnsureSuccessStatusCode();

        // Example JSON response from Python:
        // { "embedding": [0.123, -0.045, ...] }
        string responseJson = await response.Content.ReadAsStringAsync();

        // Parse JSON for "embedding"
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("embedding", out JsonElement embeddingProp) && embeddingProp.ValueKind == JsonValueKind.Array)
            {
                var vector = new List<float>();
                foreach (var item in embeddingProp.EnumerateArray())
                {
                    if (item.TryGetSingle(out float val))
                    {
                        vector.Add(val);
                    }
                }
                return vector;
            }
            else
            {
                Console.WriteLine("Warning: No 'embedding' property found in response.");
                return new List<float>();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing embedding response: {ex.Message}");
            return new List<float>();
        }
    }
}
