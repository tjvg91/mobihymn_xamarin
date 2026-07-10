using System;
using System.Collections.Generic;
using MobiHymn4.Utils;

namespace MobiHymn4.Models;

public class BoardSectionTemplate
{
    public List<string> SectionNames { get; set; } = new();
    public bool AutoApply { get; set; }
    public SectionApplyFrequency ApplyFrequency { get; set; }
    public SectionSortMode SectionSort { get; set; } = SectionSortMode.AddedOrder;
    public DateTime? AnchorDate { get; set; }
}
