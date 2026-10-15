namespace Apex.Services;
public static class SortingPresets
{
    public static readonly IReadOnlyDictionary<string, string> Items = new Dictionary<string, string>
    {
        ["Pictures"] = ".png, .jpg, .jpeg, .gif, .webp, .heic, .bmp, .tif, .tiff",
        ["Documents"] = ".pdf, .doc, .docx, .txt, .rtf, .odt, .xls, .xlsx, .ppt, .pptx",
        ["Videos"] = ".mp4, .mkv, .mov, .avi, .webm, .wmv, .m4v",
        ["Music"] = ".mp3, .wav, .flac, .aac, .m4a, .ogg",
        ["Archives"] = ".zip, .7z, .rar, .tar, .gz",
        ["Code"] = ".cs, .py, .js, .ts, .tsx, .jsx, .html, .css, .json, .java, .cpp, .h, .rs, .go"
    };
}
