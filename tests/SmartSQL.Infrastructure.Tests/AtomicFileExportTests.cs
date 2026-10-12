using System.Text;

namespace SmartSQL.Infrastructure.Tests;

public sealed class AtomicFileExportTests : IDisposable
{
    private readonly string _directory = Path.Combine(TestPaths.Root, "smartsql-export-tests", $"{Guid.NewGuid():N}");

    public AtomicFileExportTests() => Directory.CreateDirectory(_directory);

    [Xunit.Fact]
    public void PublishesCompleteFileOnlyAfterWriterReturns()
    {
        var destination = Path.Combine(_directory, "students.json");
        AtomicFileExport.Create(destination, stream =>
        {
            stream.Write(Encoding.UTF8.GetBytes("{\"name\":\"Анна\"}"));
            Xunit.Assert.False(File.Exists(destination));
            var temporary = Xunit.Assert.Single(Directory.GetFiles(_directory));
            Xunit.Assert.Equal(_directory, Path.GetDirectoryName(temporary));
        });

        Xunit.Assert.Equal("{\"name\":\"Анна\"}", File.ReadAllText(destination));
        Xunit.Assert.Equal(destination, Xunit.Assert.Single(Directory.GetFiles(_directory)));
    }

    [Xunit.Fact]
    public void WriterFailureRemovesPartialExportAndDoesNotPublish()
    {
        var destination = Path.Combine(_directory, "students.csv");
        var failure = new InvalidOperationException("Cannot serialize remaining records.");
        var reported = Xunit.Assert.Throws<InvalidOperationException>(() =>
            AtomicFileExport.Create(destination, stream =>
            {
                stream.Write(Encoding.UTF8.GetBytes("name;course\nАнна;"));
                throw failure;
            }));

        Xunit.Assert.Same(failure, reported);
        Xunit.Assert.False(File.Exists(destination));
        Xunit.Assert.Empty(Directory.GetFiles(_directory));
    }

    [Xunit.Fact]
    public void ExistingDestinationIsPreservedAndWriterIsNotInvoked()
    {
        var destination = Path.Combine(_directory, "students.csv");
        File.WriteAllText(destination, "Original export");
        var writerInvoked = false;

        Xunit.Assert.Throws<IOException>(() => AtomicFileExport.Create(destination, stream =>
        {
            writerInvoked = true;
            stream.Write(Encoding.UTF8.GetBytes("Replacement"));
        }));

        Xunit.Assert.False(writerInvoked);
        Xunit.Assert.Equal("Original export", File.ReadAllText(destination));
        Xunit.Assert.Equal(destination, Xunit.Assert.Single(Directory.GetFiles(_directory)));
    }

    [Xunit.Fact]
    public void DestinationCreatedDuringWritingIsPreservedAndTemporaryIsRemoved()
    {
        var destination = Path.Combine(_directory, "students.json");
        Xunit.Assert.Throws<IOException>(() => AtomicFileExport.Create(destination, stream =>
        {
            stream.Write(Encoding.UTF8.GetBytes("New export"));
            File.WriteAllText(destination, "Concurrent export");
        }));

        Xunit.Assert.Equal("Concurrent export", File.ReadAllText(destination));
        Xunit.Assert.Equal(destination, Xunit.Assert.Single(Directory.GetFiles(_directory)));
    }

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_directory))
            File.Delete(file);
        Directory.Delete(_directory);
    }
}
