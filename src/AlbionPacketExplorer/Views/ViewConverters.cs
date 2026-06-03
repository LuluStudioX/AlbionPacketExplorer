using Avalonia.Data.Converters;
using static AlbionPacketExplorer.Services.PacketSchemaService;

namespace AlbionPacketExplorer.Views;

public static class ViewConverters
{
    public static readonly IValueConverter BoolToOpacity =
        new FuncValueConverter<bool, double>(b => b ? 1.0 : 0.45);

    /// <summary>True when the bound value equals the converter parameter (string compare).</summary>
    public static readonly IValueConverter EqualsParam =
        new FuncValueConverter<string?, string?, bool>((v, p) => v == p);
}

public static class ParamSourceConverters
{
    public static readonly IValueConverter IsBase =
        new FuncValueConverter<ParamSource, bool>(s => s == ParamSource.Base);

    public static readonly IValueConverter IsUser =
        new FuncValueConverter<ParamSource, bool>(s => s == ParamSource.User);
}
