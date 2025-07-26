using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace Assist.Web.Services.Ingestion;

public class DataIngestor(
    ILogger<DataIngestor> logger,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    VectorStore vectorStore
)
{
    public static async Task IngestDataAsync(IServiceProvider services, IIngestionSource source)
    {
        using var scope = services.CreateScope();
        var ingestor = scope.ServiceProvider.GetRequiredService<DataIngestor>();
        await ingestor.IngestDataAsync(source);
    }

    public async Task IngestDataAsync(IIngestionSource source)
    {
        var documentsCollection = vectorStore.GetCollection<string, IngestedDocument>("Documents");
        await documentsCollection.EnsureCollectionExistsAsync();

        var chunksCollection = vectorStore.GetCollection<string, IngestedChunk>("Chunks");
        await chunksCollection.EnsureCollectionExistsAsync();

        var sourceId = source.SourceId;

        var existingDocuments = await documentsCollection.GetAsync(doc => doc.SourceId == sourceId, 100).ToListAsync().ConfigureAwait(false);
        var documents = await source.GetNewOrModifiedDocumentsAsync(existingDocuments);

        if (documents.Count() > 0)
        {
            logger.LogInformation("New or updated documents exists at {SourceId}", sourceId);
            foreach (var document in documents)
            {
                logger.LogInformation("Document: {DocumentId}, {SourceId}, {DocumentVersion}", document.DocumentId, document.SourceId, document.DocumentVersion);
                await documentsCollection.UpsertAsync(document);
                logger.LogInformation("Document inserted");

                var chunks = await source.CreateChunksForDocumentAsync(document);
                logger.LogInformation("{Count} chunks is created fot the document",chunks.Count());
                
                // Process chunks with rate limiting
                await ProcessChunksWithRateLimit(chunks, chunksCollection);

            }
        }
        else
        {
            logger.LogInformation("No new or updated documents exists");
        }
    }

    private async Task ProcessChunksWithRateLimit(IEnumerable<IngestedChunk> chunks, VectorStoreCollection<string, IngestedChunk> chunksCollection)
    {
        var semaphore = new SemaphoreSlim(5); // Limit concurrent requests
        var tasks = chunks.Select(async chunk =>
        {
            await semaphore.WaitAsync();
            try
            {
                chunk.Vector = await HandleWithRetry(chunk.Text);
                await chunksCollection.UpsertAsync(chunk);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task<Embedding<float>> HandleWithRetry(string text)
    {
        var maxRetries = 3;
        var baseDelay = TimeSpan.FromSeconds(1);

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                return await embeddingGenerator.GenerateAsync(text);
            }
            catch (Exception ex) when (IsRateLimitException(ex))
            {
                if (attempt == maxRetries - 1) throw;

                var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
                logger.LogWarning("Rate limit hit, retrying in {Delay}ms", delay.TotalMilliseconds);
                await Task.Delay(delay);
            }
        }

        throw new InvalidOperationException("Should not reach here");
    }

    private static bool IsRateLimitException(Exception ex)
    {
        return ex.Message.Contains("rate limit") ||
               ex.Message.Contains("429") ||
               (ex.InnerException?.Message.Contains("rate limit") ?? false);
    }
}
