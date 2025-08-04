namespace wsprget;

public static class SpotResultsParser
{
    public static async Task<List<Spot>> ParseSpots(string content, Func<Spot, Task<bool>> excludeFilter, CancellationToken cancellationToken)
    {
        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(content);

        var tbody = doc.DocumentNode.SelectSingleNode("body/table[3]") ?? throw new Exception("Failed to find the table body in the HTML content.");

        var result = new List<Spot>();
        foreach (var row in tbody.SelectNodes("tr").Skip(2))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return result;
            }

            var cells = row.SelectNodes("td");
            if (cells == null || cells.Count < 10)
            {
                continue; // Skip rows that do not have enough cells
            }
            var spot = new Spot
            {
                Timestamp = DateTime.Parse(cells[0].InnerText.Replace("&nbsp;", "").Trim()),
                Call = cells[1].InnerText.Replace("&nbsp;", "").Trim(),
                FrequencyHz = (long)(double.Parse(cells[2].InnerText.Replace("&nbsp;", "").Trim()) * 1000000),
                SNR = int.Parse(cells[3].InnerText.Replace("&nbsp;", "").Trim()),
                Drift = int.Parse(cells[4].InnerText.Replace("&nbsp;", "").Trim()),
                Grid = cells[5].InnerText.Replace("&nbsp;", "").Trim(),
                PowerDbm = int.Parse(cells[6].InnerText.Replace("&nbsp;", "").Trim()),
                PowerW = double.Parse(cells[7].InnerText.Replace("&nbsp;", "").Trim()),
                Reporter = cells[8].InnerText.Replace("&nbsp;", "").Trim(),
                ReporterGrid = cells[9].InnerText.Replace("&nbsp;", "").Trim(),
                DistanceKm = int.Parse(cells[10].InnerText.Replace("&nbsp;", "").Trim()),
                DistanceMi = int.Parse(cells[11].InnerText.Replace("&nbsp;", "").Trim()),
                Mode = cells[12].InnerText.Replace("&nbsp;", "").Trim(),
                Version = cells.Count > 13 ? cells[13].InnerText.Replace("&nbsp;", "").Trim() : null
            };

            if (!await excludeFilter(spot))
            {
                result.Add(spot);
            }
        }
        return result;
    }
}