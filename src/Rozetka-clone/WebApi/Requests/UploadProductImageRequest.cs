namespace WebApi.Requests
{
    public sealed class UploadProductImageRequest
    {
        public required IFormFile File { get; init; }

        public bool IsMain { get; init; }

        public int SortOrder { get; init; }
    }
}
