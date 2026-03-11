namespace Armada.Desktop.Converters
{
    using System;
    using System.Globalization;
    using Avalonia.Data.Converters;
    using Avalonia.Media;
    using Armada.Core.Enums;
    using Armada.Desktop.Models;

    /// <summary>
    /// Converts mission status to a color brush.
    /// </summary>
    public class MissionStatusColorConverter : IValueConverter
    {
        /// <inheritdoc />
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is MissionStatusEnum status)
            {
                return status switch
                {
                    MissionStatusEnum.Complete => new SolidColorBrush(Color.Parse("#4CAF50")),
                    MissionStatusEnum.InProgress => new SolidColorBrush(Color.Parse("#FFB300")),
                    MissionStatusEnum.Assigned => new SolidColorBrush(Color.Parse("#26C6DA")),
                    MissionStatusEnum.Testing => new SolidColorBrush(Color.Parse("#AB47BC")),
                    MissionStatusEnum.Review => new SolidColorBrush(Color.Parse("#FF7043")),
                    MissionStatusEnum.Failed => new SolidColorBrush(Color.Parse("#EF5350")),
                    MissionStatusEnum.Cancelled => new SolidColorBrush(Color.Parse("#9E9E9E")),
                    MissionStatusEnum.Pending => new SolidColorBrush(Color.Parse("#616161")),
                    _ => new SolidColorBrush(Color.Parse("#E0E0E0"))
                };
            }
            return new SolidColorBrush(Color.Parse("#E0E0E0"));
        }

        /// <inheritdoc />
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Converts captain state to a color brush.
    /// </summary>
    public class CaptainStateColorConverter : IValueConverter
    {
        /// <inheritdoc />
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is CaptainStateEnum state)
            {
                return state switch
                {
                    CaptainStateEnum.Idle => new SolidColorBrush(Color.Parse("#1E90FF")),
                    CaptainStateEnum.Working => new SolidColorBrush(Color.Parse("#4CAF50")),
                    CaptainStateEnum.Stalled => new SolidColorBrush(Color.Parse("#EF5350")),
                    CaptainStateEnum.Stopping => new SolidColorBrush(Color.Parse("#FFB300")),
                    _ => new SolidColorBrush(Color.Parse("#E0E0E0"))
                };
            }
            return new SolidColorBrush(Color.Parse("#E0E0E0"));
        }

        /// <inheritdoc />
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Converts voyage status to a color brush.
    /// </summary>
    public class VoyageStatusColorConverter : IValueConverter
    {
        /// <inheritdoc />
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is VoyageStatusEnum status)
            {
                return status switch
                {
                    VoyageStatusEnum.Open => new SolidColorBrush(Color.Parse("#1E90FF")),
                    VoyageStatusEnum.InProgress => new SolidColorBrush(Color.Parse("#4CAF50")),
                    VoyageStatusEnum.Complete => new SolidColorBrush(Color.Parse("#9E9E9E")),
                    VoyageStatusEnum.Cancelled => new SolidColorBrush(Color.Parse("#616161")),
                    _ => new SolidColorBrush(Color.Parse("#E0E0E0"))
                };
            }
            return new SolidColorBrush(Color.Parse("#E0E0E0"));
        }

        /// <inheritdoc />
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Converts a boolean to a connection status string.
    /// </summary>
    public class ConnectionStatusConverter : IValueConverter
    {
        /// <inheritdoc />
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool connected)
            {
                return connected ? "Connected" : "Disconnected";
            }
            return "Unknown";
        }

        /// <inheritdoc />
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Converts a boolean to a connection color brush.
    /// </summary>
    public class ConnectionColorConverter : IValueConverter
    {
        /// <inheritdoc />
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool connected)
            {
                return connected
                    ? new SolidColorBrush(Color.Parse("#4CAF50"))
                    : new SolidColorBrush(Color.Parse("#EF5350"));
            }
            return new SolidColorBrush(Color.Parse("#9E9E9E"));
        }

        /// <inheritdoc />
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Converts DiffLineTypeEnum to a foreground color for syntax highlighting.
    /// </summary>
    public class DiffLineColorConverter : IValueConverter
    {
        private static readonly SolidColorBrush _Green = new SolidColorBrush(Color.Parse("#4CAF50"));
        private static readonly SolidColorBrush _Red = new SolidColorBrush(Color.Parse("#EF5350"));
        private static readonly SolidColorBrush _Cyan = new SolidColorBrush(Color.Parse("#26C6DA"));
        private static readonly SolidColorBrush _Gray = new SolidColorBrush(Color.Parse("#B0B0B0"));

        /// <inheritdoc />
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is DiffLineTypeEnum lineType)
            {
                return lineType switch
                {
                    DiffLineTypeEnum.Addition => _Green,
                    DiffLineTypeEnum.Deletion => _Red,
                    DiffLineTypeEnum.Hunk => _Cyan,
                    _ => _Gray
                };
            }
            return _Gray;
        }

        /// <inheritdoc />
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Converts DiffLineTypeEnum to a subtle background color.
    /// </summary>
    public class DiffLineBgConverter : IValueConverter
    {
        private static readonly SolidColorBrush _GreenBg = new SolidColorBrush(Color.Parse("#0D2E1A"));
        private static readonly SolidColorBrush _RedBg = new SolidColorBrush(Color.Parse("#2E0D0D"));
        private static readonly SolidColorBrush _HunkBg = new SolidColorBrush(Color.Parse("#0D2E2E"));
        private static readonly SolidColorBrush _Transparent = new SolidColorBrush(Colors.Transparent);

        /// <inheritdoc />
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is DiffLineTypeEnum lineType)
            {
                return lineType switch
                {
                    DiffLineTypeEnum.Addition => _GreenBg,
                    DiffLineTypeEnum.Deletion => _RedBg,
                    DiffLineTypeEnum.Hunk => _HunkBg,
                    _ => _Transparent
                };
            }
            return _Transparent;
        }

        /// <inheritdoc />
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
