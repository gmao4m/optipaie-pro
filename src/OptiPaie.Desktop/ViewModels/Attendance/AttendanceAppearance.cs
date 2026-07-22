using System.Collections.Generic;
using System.Windows.Media;
using OptiPaie.Core.Enums;

namespace OptiPaie.Desktop.ViewModels.Attendance
{
    /// <summary>
    /// The single source of truth for how an attendance status looks in the matrix:
    /// its colour, its one-letter code and its French label. Kept in one place so the
    /// grid cells, the status palette and the legend stay consistent.
    /// </summary>
    public static class AttendanceAppearance
    {
        private static Brush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        // Cell fills (soft, high-contrast against dark text).
        public static readonly Brush PresentFill = Frozen(0xA5, 0xD6, 0xA7); // green
        public static readonly Brush AbsentFill  = Frozen(0xEF, 0x9A, 0x9A); // red
        public static readonly Brush LateFill     = Frozen(0xFF, 0xCC, 0x80); // orange
        public static readonly Brush MissionFill  = Frozen(0x90, 0xCA, 0xF9); // blue
        public static readonly Brush LeaveFill     = Frozen(0xCE, 0x93, 0xD8); // purple
        public static readonly Brush HolidayFill   = Frozen(0xFF, 0xF5, 0x9D); // yellow
        public static readonly Brush WeekendFill   = Frozen(0xCF, 0xD8, 0xDC); // gray
        public static readonly Brush EmptyFill     = Frozen(0xFF, 0xFF, 0xFF); // white
        public static readonly Brush FutureFill    = Frozen(0xF4, 0xF6, 0xF8); // very light

        /// <summary>Cell background, given the (optional) status, weekend and future flags.</summary>
        public static Brush Background(AttendanceStatus? status, bool isWeekend, bool isFuture)
        {
            if (status.HasValue)
            {
                return Fill(status.Value);
            }

            if (isWeekend) return WeekendFill;
            if (isFuture) return FutureFill;
            return EmptyFill;
        }

        public static Brush Fill(AttendanceStatus status)
        {
            switch (status)
            {
                case AttendanceStatus.Present: return PresentFill;
                case AttendanceStatus.Absent: return AbsentFill;
                case AttendanceStatus.Late: return LateFill;
                case AttendanceStatus.Mission: return MissionFill;
                case AttendanceStatus.Leave: return LeaveFill;
                case AttendanceStatus.Holiday: return HolidayFill;
                case AttendanceStatus.Rest: return WeekendFill;
                default: return EmptyFill;
            }
        }

        /// <summary>One-letter code shown inside a cell.</summary>
        public static string Letter(AttendanceStatus? status, bool isWeekend)
        {
            if (status.HasValue)
            {
                switch (status.Value)
                {
                    case AttendanceStatus.Present: return "P";
                    case AttendanceStatus.Absent: return "A";
                    case AttendanceStatus.Late: return "R";
                    case AttendanceStatus.Mission: return "M";
                    case AttendanceStatus.Leave: return "C";
                    case AttendanceStatus.Holiday: return "F";
                    case AttendanceStatus.Rest: return "W";
                }
            }

            return isWeekend ? "W" : string.Empty;
        }

        public static string Label(AttendanceStatus status)
        {
            switch (status)
            {
                case AttendanceStatus.Present: return "Présent";
                case AttendanceStatus.Absent: return "Absent";
                case AttendanceStatus.Late: return "Retard";
                case AttendanceStatus.Mission: return "Mission";
                case AttendanceStatus.Leave: return "Congé payé";
                case AttendanceStatus.Holiday: return "Jour férié";
                case AttendanceStatus.Rest: return "Repos / week-end";
                default: return string.Empty;
            }
        }

        /// <summary>The palette order shown in the toolbar (the paintable statuses).</summary>
        public static IReadOnlyList<AttendanceStatus> Palette { get; } = new[]
        {
            AttendanceStatus.Present,
            AttendanceStatus.Absent,
            AttendanceStatus.Late,
            AttendanceStatus.Mission,
            AttendanceStatus.Leave,
            AttendanceStatus.Holiday,
            AttendanceStatus.Rest
        };
    }
}
