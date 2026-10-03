using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Nexus.ProjectManagement.Waterfall.Domain;
using NexusCore.SharedKernel.Results;

namespace Nexus.ProjectManagement.Waterfall.Application.MsProject;

/// <summary>
/// Reads the MS Project XML format (MSPDI). Element names are matched without their namespace so
/// files with or without it both load, and unknown elements are ignored. The XML is read with
/// DTDs prohibited and no external resolver, so a hostile file cannot pull in other files or
/// expand entities.
/// </summary>
public static class MsProjectXmlReader
{
    public const int MaxTasks = 5_000;
    public const int DefaultMinutesPerDay = 480;

    private static readonly Regex IsoDuration = new(
        @"^P(?:(?<d>\d+)D)?(?:T(?:(?<h>\d+)H)?(?:(?<m>\d+)M)?(?:(?<s>\d+(?:\.\d+)?)S)?)?$", RegexOptions.Compiled);

    public static Result<MsProjectPlan> Read(Stream xml)
    {
        XDocument document;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 100_000_000
            };
            using var reader = XmlReader.Create(xml, settings);
            document = XDocument.Load(reader);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            return Result.Failure<MsProjectPlan>(Error.Validation("The file is not valid MS Project XML."));
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "Project")
        {
            return Result.Failure<MsProjectPlan>(Error.Validation("The file is not an MS Project XML file (no <Project> element)."));
        }

        var minutesPerDay = ParseInt(Child(root, "MinutesPerDay")) is > 0 and var configured ? configured : DefaultMinutesPerDay;
        var taskElements = root.Elements().Where(e => e.Name.LocalName == "Tasks")
            .SelectMany(tasks => tasks.Elements().Where(e => e.Name.LocalName == "Task")).ToList();
        if (taskElements.Count > MaxTasks + 1)
        {
            return Result.Failure<MsProjectPlan>(Error.Validation($"The file has more than {MaxTasks} tasks."));
        }

        var tasks = new List<MsProjectTask>();
        foreach (var element in taskElements)
        {
            var uid = ParseInt(Child(element, "UID"));
            // UID 0 is MS Project's own project-summary row; a null row is an empty line in the sheet.
            if (uid is null or 0 || Child(element, "IsNull") == "1")
            {
                continue;
            }

            tasks.Add(new MsProjectTask(
                uid.Value,
                Child(element, "Name")?.Trim() ?? string.Empty,
                Math.Max(ParseInt(Child(element, "OutlineLevel")) ?? 1, 1),
                Child(element, "Milestone") == "1",
                Child(element, "Summary") == "1",
                ParseDate(Child(element, "Start")),
                ParseDate(Child(element, "Finish")),
                ParseDurationMinutes(Child(element, "Duration"), minutesPerDay),
                Math.Clamp(ParseInt(Child(element, "PercentComplete")) ?? 0, 0, 100),
                element.Elements().Where(e => e.Name.LocalName == "PredecessorLink").Select(ReadLink).OfType<MsProjectLink>().ToList()));
        }

        if (tasks.Count == 0)
        {
            return Result.Failure<MsProjectPlan>(Error.Validation("The file contains no tasks."));
        }

        return Result.Success(new MsProjectPlan(Child(root, "Title") ?? Child(root, "Name"), ParseDate(Child(root, "StartDate")), minutesPerDay, tasks));
    }

    private static MsProjectLink? ReadLink(XElement element)
    {
        if (ParseInt(Child(element, "PredecessorUID")) is not { } predecessor)
        {
            return null;
        }

        // MSPDI numbers link types 0 = FF, 1 = FS, 2 = SF, 3 = SS; a missing one means FS.
        var type = ParseInt(Child(element, "Type")) switch
        {
            0 => DependencyType.FinishToFinish,
            2 => DependencyType.StartToFinish,
            3 => DependencyType.StartToStart,
            _ => DependencyType.FinishToStart
        };

        // LinkLag is in tenths of a minute.
        var lagMinutes = (int)Math.Round((ParseLong(Child(element, "LinkLag")) ?? 0) / 10.0);
        return new MsProjectLink(predecessor, type, lagMinutes);
    }

    private static string? Child(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

    private static int? ParseInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static long? ParseLong(string? text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    /// <summary>The date part of "2026-03-02T08:00:00"; the time of day is not kept.</summary>
    private static DateOnly? ParseDate(string? text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.NoCurrentDateDefault, out var value)
            ? DateOnly.FromDateTime(value)
            : null;

    /// <summary>"PT40H0M0S" -> 2400. A day component counts as one working day.</summary>
    public static int? ParseDurationMinutes(string? text, int minutesPerDay)
    {
        if (string.IsNullOrWhiteSpace(text) || IsoDuration.Match(text.Trim()) is not { Success: true } match)
        {
            return null;
        }

        static double Part(Group group) => group.Success ? double.Parse(group.Value, CultureInfo.InvariantCulture) : 0;
        var minutes = Part(match.Groups["d"]) * minutesPerDay + Part(match.Groups["h"]) * 60 + Part(match.Groups["m"]) + Part(match.Groups["s"]) / 60;
        return (int)Math.Round(minutes);
    }
}
