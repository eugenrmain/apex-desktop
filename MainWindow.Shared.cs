using Apex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using System.Diagnostics;

namespace Apex;
public sealed partial class MainWindow
{
    private static class Ui
    {
        public const double PageTitle = 34, SectionTitle = 18, WidgetTitle = 16, Body = 14, Supporting = 12;
        public const double SelectionHeight = 190, Gap = 16, Padding = 20;
    }

    private UIElement OutputCard(CompressionQueueItem item)
    {
        var content = Section("Saved to");
        var result = item.Output!;
        content.Children.Add(Text(Path.GetFileName(result.Output), Ui.WidgetTitle, "#EDF3FF", true));
        content.Children.Add(Text(Path.GetDirectoryName(result.Output)!, Ui.Body, "#B5C5DE"));
        content.Children.Add(Text(FileToolkit.Size(result.OriginalBytes) + " → " + FileToolkit.Size(result.OutputBytes), 24, "#83DDFF", true));
        double saving = result.OriginalBytes == 0 ? 0 : 100 * (1 - result.OutputBytes / (double)result.OriginalBytes);
        content.Children.Add(Text(saving > 0 ? $"{saving:0.#}% smaller" : "This copy is not smaller at these settings.", Ui.Body, "#A9BAD4"));
        content.Children.Add(Text(result.Details, Ui.Supporting, "#A9BAD4"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        void Safe(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e) { ShowTaskNotice("Could not open output", e.Message, NoticeKind.Error); }
        }
        actions.Children.Add(ActionButton("\uE768", "Open file", (_, _) => Safe(() => Process.Start(new ProcessStartInfo(result.Output) { UseShellExecute = true }))));
        actions.Children.Add(ActionButton("\uE8B7", "Show in folder", (_, _) => Safe(() =>
        {
            if (!File.Exists(result.Output))
                throw new FileNotFoundException("The output file has moved or was removed.");
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + result.Output + "\"") { UseShellExecute = true });
        })));
        actions.Children.Add(ActionButton("\uE8C8", "Copy path", (_, _) => Safe(() => { var data = new DataPackage(); data.SetText(result.Output); Clipboard.SetContent(data); })));
        actions.SizeChanged += (_, _) => actions.Orientation = content.ActualWidth < 460 ? Orientation.Vertical : Orientation.Horizontal;
        content.Children.Add(actions);
        return Glass(content);
    }

    private void FinishTask(TaskOutcome outcome)
    {
        status.Text = outcome.Message;
        taskCompletion = (outcome.Message, outcome.Kind switch
        {
            TaskOutcomeKind.Success => NoticeKind.Success,
            TaskOutcomeKind.Partial => NoticeKind.Warning,
            TaskOutcomeKind.Canceled => NoticeKind.Information,
            _ => NoticeKind.Error
        });
    }
}
