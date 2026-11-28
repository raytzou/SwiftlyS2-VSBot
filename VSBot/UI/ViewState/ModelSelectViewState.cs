namespace VSBot.UI.ViewState
{
    internal sealed class ModelSelectViewState
    {
        public required ulong SessionId { get; init; }
        public required (string Name, string? ImageKey)[] Models { get; init; }
        public int Page { get; set; }
        public string? SelectedName { get; set; }
    }
}
