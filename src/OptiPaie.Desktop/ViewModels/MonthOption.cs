namespace OptiPaie.Desktop.ViewModels
{
    /// <summary>A month number with its (localized) name, used by the month pickers across the app.</summary>
    public sealed class MonthOption
    {
        public MonthOption(int value, string label) { Value = value; Label = label; }
        public int Value { get; }
        public string Label { get; }
    }
}
