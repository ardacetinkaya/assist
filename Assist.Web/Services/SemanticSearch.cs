using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.MongoDB;

namespace Assist.Web.Services;

public class SemanticSearch(MongoVectorStore vectorStore)
{
    public async Task<IReadOnlyList<IngestedChunk>> SearchAsync(string text, string? documentIdFilter, int maxResults)
    {
        var vectorCollection = vectorStore.GetCollection<float, IngestedChunk>("Chunks");
        await vectorCollection.EnsureCollectionExistsAsync();
        var nearest = vectorCollection.SearchAsync(text, maxResults, new VectorSearchOptions<IngestedChunk>
        {
            Filter = documentIdFilter is { Length: > 0 } ? record => record.DocumentId == documentIdFilter : null,
        });

        return await nearest.Select(result => result.Record).ToListAsync();
    }
}
