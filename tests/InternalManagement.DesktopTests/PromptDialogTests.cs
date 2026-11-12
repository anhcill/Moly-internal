using InternalManagement.Desktop.Services;

namespace InternalManagement.DesktopTests;

public sealed class PromptDialogTests
{
    private static readonly PromptField SourceClassField = new(
        "sourceClassId",
        "Lớp Live nguồn",
        IsRequired: false,
        RequiredWhenKey: "learningMode",
        RequiredWhenValue: "RECORDING_MIRROR");

    [Theory]
    [InlineData("LIVE", false)]
    [InlineData("RECORDING_INDEPENDENT", false)]
    [InlineData("RECORDING_MIRROR", true)]
    public void IsFieldRequired_SourceClass_DependsOnRecordingMode(string learningMode, bool expected)
    {
        var values = new Dictionary<string, string> { ["learningMode"] = learningMode };

        Assert.Equal(expected, PromptDialog.IsFieldRequired(SourceClassField, values));
    }
}
