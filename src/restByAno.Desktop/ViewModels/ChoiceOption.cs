namespace restByAno.Desktop.ViewModels;

/// <summary>A value with the label shown in a ComboBox.</summary>
public sealed record ChoiceOption<T>(T Value, string Label);
