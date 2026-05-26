namespace FluxCad48.Sheets
{
	public class SheetArrangeOptions
	{
		public int MaxColumnsPerRow { get; set; }

		public double ColumnGap { get; set; }

		public double RowGap { get; set; }

		public double StartGapFromSource { get; set; }

		public bool AlignBottom { get; set; }

		public bool DrawYellowMarkerOnSource { get; set; }

		public SheetArrangeOptions()
		{
			MaxColumnsPerRow = 20;
			ColumnGap = 100.0;
			RowGap = 1200.0;
			StartGapFromSource = 300.0;
			AlignBottom = true;
			DrawYellowMarkerOnSource = true;
		}
	}
}