using System.Collections.Generic;

namespace OptiPaie.Core.Certificates
{
    /// <summary>
    /// Absolute-coordinate layout for printing ATS/DRT values ONTO the pre-printed CNAS form.
    /// Every position is in millimetres from the top-left of the page, so the output overlays
    /// the paper form exactly. Loaded from a loose JSON file (Assets/AtsDrtTemplates/form-layout.json)
    /// so a support agent can nudge a coordinate WITHOUT recompiling.
    /// </summary>
    public sealed class FormLayoutConfig
    {
        public Dictionary<string, FormDefinition> Forms { get; set; } = new Dictionary<string, FormDefinition>();
    }

    public sealed class FormDefinition
    {
        public List<FormPage> Pages { get; set; } = new List<FormPage>();
    }

    public sealed class FormPage
    {
        public double WidthMm { get; set; } = 210;
        public double HeightMm { get; set; } = 297;

        /// <summary>File name of the blank-form scan (Assets/AtsDrtTemplates) — used ONLY for the
        /// visual proof overlay, never printed onto the client's pre-printed form.</summary>
        public string BackgroundImage { get; set; }

        public List<FormField> Fields { get; set; } = new List<FormField>();
    }

    /// <summary>
    /// One printed value. <see cref="Type"/> drives placement:
    /// <list type="bullet">
    /// <item><b>grid</b>: one character per cell — <see cref="XMm"/> is the CENTRE x of the first
    /// cell, <see cref="PitchMm"/> the centre-to-centre spacing, <see cref="Cells"/> the count;
    /// each character is centred in its cell (never a block string).</item>
    /// <item><b>checkbox</b>: a mark centred in the little square at (<see cref="XMm"/>,<see cref="YMm"/>).</item>
    /// <item><b>rectangle</b>: value framed inside a box [XMm..XMm+WidthMm] × height HeightMm,
    /// auto-shrunk to fit.</item>
    /// <item><b>dotted</b> (default): value starts at XMm on baseline YMm, auto-shrunk to
    /// <see cref="MaxWidthMm"/> so it never overflows the dotted zone.</item>
    /// </list>
    /// </summary>
    public sealed class FormField
    {
        public string Name { get; set; }        // value key (e.g. "DATEN", "NMS", "Case1")
        public string Type { get; set; } = "dotted";

        public double XMm { get; set; }          // grid: first cell centre; else left/anchor x
        public double YMm { get; set; }          // text baseline y (grid/dotted); box/checkbox centre y

        public double MaxWidthMm { get; set; }   // dotted: shrink threshold
        public double WidthMm { get; set; }      // rectangle: box width
        public double HeightMm { get; set; }     // rectangle: box height (for vertical centring)

        public double PitchMm { get; set; }      // grid: centre-to-centre cell spacing
        public int Cells { get; set; }           // grid: number of cells

        public double FontSize { get; set; } = 10; // points
        public bool Bold { get; set; }
        public string Align { get; set; } = "left"; // rectangle/dotted: "left" | "center"
    }
}
