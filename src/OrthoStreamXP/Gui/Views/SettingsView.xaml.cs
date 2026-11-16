using System.Windows.Controls;

namespace OrthoStreamXP.Gui;

internal sealed partial class SettingsView
{
    public SettingsView()
    {
        InitializeComponent();
        // The colour preview stays in sight while its settings are changed below it
        _ = new StickyHeader(Scroller, PreviewPin, ShowPreviewBackdrop);
    }

    /// <summary>Pinned, the preview covers the settings under it: it gets the page's own (opaque) backdrop.</summary>
    private void ShowPreviewBackdrop(bool pinned)
    {
        if (pinned)
        {
            PreviewPin.SetResourceReference(Border.BackgroundProperty, "SolidBackgroundFillColorBaseBrush");
            PreviewPinLayer.SetResourceReference(Border.BackgroundProperty, "LayerFillColorDefaultBrush");
        }
        else
        {
            PreviewPin.ClearValue(Border.BackgroundProperty);
            PreviewPinLayer.ClearValue(Border.BackgroundProperty);
        }
    }
}
