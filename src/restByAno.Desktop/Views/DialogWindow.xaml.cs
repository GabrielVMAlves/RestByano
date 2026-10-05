using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using restByAno.Desktop.Services;

namespace restByAno.Desktop.Views;

/// <summary>App-owned, dark-themed message/choice/input dialog. Native OS file dialogs remain OS-controlled.</summary>
public partial class DialogWindow : Window
{
    private readonly bool _needsInput;
    private readonly bool _needsChoice;

    public DialogWindow(DialogRequest request)
    {
        InitializeComponent();
        TitleText.Text = request.Title;
        MessageText.Text = request.Message;

        if (!string.IsNullOrEmpty(request.Detail))
        {
            DetailText.Text = request.Detail;
            DetailText.Visibility = Visibility.Visible;
        }

        if (request.InputText is not null)
        {
            _needsInput = true;
            InputBox.Text = request.InputText;
            InputBox.Visibility = Visibility.Visible;
            InputBox.SelectAll();
        }

        if (request.Choices is not null)
        {
            _needsChoice = true;
            foreach (var choice in request.Choices)
                ChoiceList.Items.Add(choice);
            if (ChoiceList.Items.Count > 0)
                ChoiceList.SelectedIndex = 0;
            ChoiceList.Visibility = Visibility.Visible;
        }

        for (var i = 0; i < request.Buttons.Count; i++)
        {
            var spec = request.Buttons[i];
            var index = i;
            var button = new Button
            {
                Content = spec.Label,
                IsDefault = spec.IsDefault,
                IsCancel = spec.IsCancel,
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                MinWidth = 90,
            };
            AutomationProperties.SetName(button, spec.Label);
            if (spec.IsDefault)
                button.Style = (Style)FindResource("AccentButton");
            button.Click += (_, _) =>
            {
                if (!spec.IsCancel && !InputIsValid())
                    return; // keep the dialog open until the entry is usable

                ChosenIndex = index;
                InputValue = _needsInput ? InputBox.Text : null;
                SelectedChoice = _needsChoice ? ChoiceList.SelectedIndex : -1;
                DialogResult = true;
            };
            ButtonPanel.Children.Add(button);

            if (spec.IsCancel && ChosenIndex < 0)
                ChosenIndex = index; // closing with Esc / the title bar counts as the cancel button
        }

        DarkTitleBar.Apply(this);
        Loaded += (_, _) =>
        {
            if (_needsInput)
            {
                InputBox.Focus();
                return;
            }

            if (_needsChoice)
            {
                ChoiceList.Focus();
                return;
            }

            var target = ButtonPanel.Children.OfType<Button>().FirstOrDefault(b => b.IsDefault)
                         ?? ButtonPanel.Children.OfType<Button>().FirstOrDefault();
            target?.Focus();
        };
    }

    public int ChosenIndex { get; private set; } = -1;
    public string? InputValue { get; private set; }
    public int SelectedChoice { get; private set; } = -1;

    private bool InputIsValid() =>
        (!_needsInput || !string.IsNullOrWhiteSpace(InputBox.Text)) && (!_needsChoice || ChoiceList.SelectedIndex >= 0);
}
