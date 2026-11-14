using AwesomeAssertions;
using static TinapayanRMS.Components.OneCopyPerPc;

namespace TinapayanRMS.Components.Tests;

/// <summary>
/// OneCopyPerPc: a copy starting in one Windows sign-in is refused while another sign-in has the app open; a copy in its
/// own sign-in is left to the one-copy-per-folder rule, so a copy built from source still runs beside the installed one
/// there.
/// </summary>
public class OneCopyPerPcTests
{
    private const int Me = 100;
    private const int MySession = 2;

    [Test]
    public void Alone_it_runs()
    {
        RefusalFor([new RunningCopy(Me, MySession)], Me, MySession).Should().BeNull();
    }

    [Test]
    public void Beside_another_copy_in_its_own_sign_in_it_runs()
    {
        RefusalFor([new RunningCopy(Me, MySession), new RunningCopy(200, MySession)], Me, MySession).Should().BeNull();
    }

    [Test]
    public void While_another_sign_in_has_the_app_open_it_says_so()
    {
        RefusalFor([new RunningCopy(Me, MySession), new RunningCopy(200, 3)], Me, MySession).Should().Be(OtherSignInText);
    }
}
