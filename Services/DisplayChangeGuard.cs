namespace Apex.Services;
public static class DisplayChangeGuard
{
    public static async Task<bool> ApplyAsync(Func<string> read, Action<string, string> write, string before, string requested, Func<Task<bool>> confirm, TimeSpan timeout)
    {
        write(requested, before);
        bool keep = false;
        try
        {
            keep = await confirm().WaitAsync(timeout);
            if (keep && read() != requested)
                throw new IOException("Display changed outside Apex during confirmation.");
        }
        catch (TimeoutException) { keep = false; }
        finally
        {
            if (!keep)
            {
                string current = read();
                if (current == requested)
                    write(before, requested);
                else if (current != before)
                    throw new IOException("Display restoration conflict: changed outside Apex; left untouched.");
            }
        }
        return keep;
    }
}
