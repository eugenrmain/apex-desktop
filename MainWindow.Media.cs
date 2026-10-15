using Apex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;

namespace Apex;

public sealed partial class MainWindow
{
    private string conversionSource = "";
    private AudioConversionResult? conversionResult;

    private void ConvertMedia()
    {
        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(Text("MP4 → MP3", Ui.SectionTitle, "#EDF3FF", true));
        heading.Children.Add(Text("Extract the audio. Keep the original video.", Ui.Supporting, "#A9BAD4"));
        page.Children.Add(heading);
        var input = PathPicker("Video to convert", out var source, true, fileExtension: ".mp4");
        page.Children.Add(input);
        var details = Text("", Ui.Supporting, "#A9BAD4");
        details.Visibility = Visibility.Collapsed;
        page.Children.Add(details);
        var outputs = new StackPanel { Spacing = 10 };
        int revision = 0;
        Task inspection = Task.CompletedTask;
        UIElement? convertAction = null;

        void RenderResult()
        {
            outputs.Children.Clear();
            if (conversionResult is not { } result)
                return;
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(Text(Path.GetFileName(result.Output), Ui.WidgetTitle, "#83DDFF", true));
            string duration = result.Duration.ToString(result.Duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
            content.Children.Add(Text($"MP3 · {duration} · {FileToolkit.Size(result.Bytes)}", Ui.Supporting, "#A9BAD4"));
            ToolTipService.SetToolTip(content, result.Output);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            void Open(bool folder)
            {
                try
                {
                    if (!File.Exists(result.Output))
                        throw new FileNotFoundException("The converted file has moved or was removed.");
                    Process.Start(folder ? new ProcessStartInfo("explorer.exe", "/select,\"" + result.Output + "\"") { UseShellExecute = true } : new ProcessStartInfo(result.Output) { UseShellExecute = true });
                }
                catch (Exception e) { ShowTaskNotice("Could not open output", e.Message, NoticeKind.Error); }
            }
            actions.Children.Add(ActionButton("\uE768", "Open MP3", (_, _) => Open(false)));
            var folder = ActionButton("\uE8B7", "Show in folder", (_, _) => Open(true));
            folder.Content = Icon("\uE8B7", 16, "#DEEAFF");
            ToolTipService.SetToolTip(folder, "Show in folder");
            actions.Children.Add(folder);
            content.Children.Add(actions);
            outputs.Children.Add(Glass(content));
        }
        async Task SelectSource()
        {
            int version = ++revision;
            string selected = source.Text;
            conversionSource = selected;
            conversionResult = null;
            outputs.Children.Clear();
            details.Visibility = Visibility.Collapsed;
            if (convertAction is not null)
                convertAction.Visibility = Visibility.Collapsed;
            if (string.IsNullOrWhiteSpace(selected))
                return;
            try
            {
                if (!Path.GetExtension(selected).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Choose an MP4 file.");
                var entry = await Task.Run(() => FileToolkit.Inspect(selected));
                if (version != revision || currentPage != "Convert")
                    return;
                details.Text = FileToolkit.Size(entry.Size) + " · MP3 audio, 192 kbps · Save beside original";
                ToolTipService.SetToolTip(details, Path.ChangeExtension(entry.Path, ".mp3") + " (a numbered copy is used if it already exists)");
                details.Visibility = Visibility.Visible;
                if (convertAction is not null && conversionResult is null)
                    convertAction.Visibility = Visibility.Visible;
            }
            catch (Exception e) { if (version == revision && currentPage == "Convert") ShowTaskNotice("Could not select video", e.Message, NoticeKind.Error); }
        }
        convertAction = Work("Convert to MP3", async (token, progress) =>
        {
            await inspection;
            conversionResult = await MediaConverter.ExtractMp3Async(source.Text, progress, token);
            RenderResult();
            convertAction!.Visibility = Visibility.Collapsed;
            FinishTask("MP3 saved beside the original");
        }, true);
        convertAction.Visibility = Visibility.Collapsed;
        page.Children.Add(convertAction);
        page.Children.Add(outputs);
        source.RegisterPropertyChangedCallback(TextBox.TextProperty, (_, _) => inspection = SelectSource());
        string remembered = conversionSource;
        var previousResult = conversionResult;
        source.Text = remembered;
        conversionResult = previousResult;
        RenderResult();
        if (previousResult is not null)
            convertAction.Visibility = Visibility.Collapsed;
    }
}
