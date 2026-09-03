namespace api.ViewModels
{
    public class InlineRenameViewModel
    {
        public required string Action { get; init; }
        public required string IdFieldName { get; init; }
        public required int IdValue { get; init; }
        public required string NameFieldName { get; init; }
        public string? CurrentName { get; init; }
        public int MaxLength { get; init; } = 100;
    }
}
