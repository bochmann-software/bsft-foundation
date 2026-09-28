using Microsoft.EntityFrameworkCore;

namespace Bsft.Testing.Tests.Support;

public class TestNote
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;
}
