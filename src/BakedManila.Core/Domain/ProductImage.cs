namespace BakedManila.Core.Domain;

public class ProductImage
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public required string BlobName { get; set; }
    public int SortOrder { get; set; }
}
