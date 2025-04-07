using System.Text.Json;
using System.Text;
using GmailDownloader.Contracts;
using Microsoft.AspNetCore.Builder;
using Contracts;
using Api;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Reflection.Metadata;


var builder = WebApplication.CreateBuilder(args);

// 1) Load embeddings from JSON files (with base64-encoded vectors)
//    We'll load all *.json in a given directory, but you can adapt as needed.
string embeddingsDir = @"c:\Temp\svj\embeddings\";

// We'll accumulate all embedded emails into a single in-memory list
List<EmbeddedEmail> allEmbeddings = new List<EmbeddedEmail>();

if (Directory.Exists(embeddingsDir))
{
    foreach (string file in Directory.GetFiles(embeddingsDir, "*.json", SearchOption.TopDirectoryOnly))
    {
        string json = File.ReadAllText(file);
        var items = JsonSerializer.Deserialize<List<EmbeddedEmail>>(json);
        if (items != null)
        {
            // Decode each base64 vector once, store in memory
            foreach (var item in items)
            {
                item.Vector = FloatBase64Helper.ConvertBase64ToFloatList(item.VectorBase64);
            }
            allEmbeddings.AddRange(items);
        }
    }
}
else
{
    Console.WriteLine($"Embeddings directory not found: {embeddingsDir}");
}

// 2) Add HttpClient as a singleton for calling Python embed server
builder.Services.AddSingleton<HttpClient>();

// 3) Add our data as a singleton for easy access in the endpoint
builder.Services.AddSingleton(allEmbeddings);

var app = builder.Build();

app.MapGet("/", () =>
{
    // Return some minimal HTML with:
    // - a text area
    // - a button that triggers fetch() to /search
    // - a <div> to display the results table

    string html = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8' />
    <title>Embedding Search Demo</title>
</head>
<body>
    <h1>Search Embeddings</h1>
    <form id='searchForm'>
        <p>
            <label for='textInput'>Enter your query text:</label><br/>
            <textarea id='textInput' rows='4' cols='80'></textarea>
        </p>
        <p>
            <button type='submit'>Search</button>
        </p>
    </form>

    <div id='results'></div>

    <script>
        document.getElementById('searchForm').addEventListener('submit', async function(e) {
            e.preventDefault();
            const text = document.getElementById('textInput').value;

            // Call POST /search with JSON body
            const response = await fetch('/search', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ text })
            });

            if (!response.ok) {
                alert('Error in search: ' + response.statusText);
                return;
            }

            // Parse JSON: we expect an array of { threadId, similarity }
            const data = await response.json();

            // Build a small table with the results
            let htmlTable = '<table border=""1""><tr><th>Similarity</th><th>Thread Link</th></tr>';
            for (let item of data)
    {
        let link = 'https://mail.google.com/mail/u/0/#inbox/' + item.threadId;
        // Round similarity to 3 decimals
        let similarityStr = item.similarity.toFixed(3);
        htmlTable += `<tr>

                        <td>${ similarityStr}</td>

                        <td><a href='${link}' target='_blank'>${ item.threadId}</a></td>

                      </tr>`;
    }
    htmlTable += '</table>';

    document.getElementById('results').innerHTML = htmlTable;
});
    </script>
</body>
</html>
";
    return Results.Text(html, "text/html");
});

// Minimal API endpoint: POST /search
// Receives { "text": "some input" } and returns top 3 matches
app.MapPost("/search", async (SearchRequest request, HttpClient http, List<EmbeddedEmail> embeddings) =>
{
    // Step A: Embed the incoming text by calling Python server
    //  - We assume you have a running Python embed service at http://localhost:8000/
    string pythonUrl = "http://localhost:8000/";

    // Build the JSON for the POST body
    var payloadObj = new { text = request.Text };
    string payloadJson = JsonSerializer.Serialize(payloadObj);

    var stringContent = new StringContent(payloadJson, Encoding.UTF8, "application/json");
    var response = await http.PostAsync(pythonUrl, stringContent);
    response.EnsureSuccessStatusCode();

    // Example Python response:
    // { "embedding": [0.123, -0.234, ...] }
    string responseJson = await response.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(responseJson);
    JsonElement root = doc.RootElement;
    JsonElement embeddingElement = root.GetProperty("embedding");

    // Convert to List<float>
    List<float> inputVector = new List<float>();
    foreach (var item in embeddingElement.EnumerateArray())
    {
        if (item.TryGetSingle(out float val))
            inputVector.Add(val);
    }

    // Step B: Compute similarity to each embedding in memory
    //         (cosine similarity = dot_product / (norm(A)*norm(B)))
    //         We'll store highest => pick top 3
    var results = embeddings
        .Select(e =>
        {
            float sim = CosineSimilarity(inputVector, e.Vector);
            return new { e.ThreadId, Similarity = sim };
        })
        .OrderByDescending(x => x.Similarity)  // highest similarity first
        .Take(3)
        .Select(x => new SearchResult { ThreadId = x.ThreadId, Similarity = x.Similarity })
        .ToList();

    return Results.Ok(results);
});

app.Run();

// Utility function for cosine similarity
static float CosineSimilarity(List<float> a, List<float> b)
{
    // Handle edge cases
    if (a.Count == 0 || b.Count == 0 || a.Count != b.Count)
        return 0f;

    float dot = 0f;
    float normA = 0f;
    float normB = 0f;

    for (int i = 0; i < a.Count; i++)
    {
        dot += a[i] * b[i];
        normA += a[i] * a[i];
        normB += b[i] * b[i];
    }

    normA = (float)Math.Sqrt(normA);
    normB = (float)Math.Sqrt(normB);

    if (Math.Abs(normA) < 1e-8 || Math.Abs(normB) < 1e-8)
        return 0f;

    return dot / (normA * normB);
}
