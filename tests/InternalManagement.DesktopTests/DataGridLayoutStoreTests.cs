using System.IO;
using InternalManagement.Desktop.Services;

namespace InternalManagement.DesktopTests;

public sealed class DataGridLayoutStoreTests
{
    [Fact]
    public void SaveColumnWidth_PersistsAcrossStoreInstances()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"moly-layout-tests-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "table-layouts.json");

        try
        {
            var writer = new DataGridLayoutStore(filePath);
            writer.SaveColumnWidth("CoursesView:CoursesDataGrid", "binding:Title", 287.45);

            var reader = new DataGridLayoutStore(filePath);

            Assert.Equal(287.45, reader.GetColumnWidth("CoursesView:CoursesDataGrid", "binding:Title"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void GetColumnWidth_InvalidJson_UsesDefaultLayout()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"moly-layout-tests-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "table-layouts.json");

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(filePath, "{not-json");

            var store = new DataGridLayoutStore(filePath);

            Assert.Null(store.GetColumnWidth("CoursesView:CoursesDataGrid", "binding:Title"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
