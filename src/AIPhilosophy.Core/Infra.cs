using Microsoft.AspNetCore.Identity;

namespace AIPhilosophy.Core.Interfaces;

public interface ITokenService
{
    string CreateToken(IdentityUser user, IList<string> roles);
}

public interface ICronService
{
    string BuildDailyCron(int hourUtc, int minuteUtc, List<string> daysOfWeek);
    List<(int Hour, int Minute)> ParsePostingTimes(string csvTimes);
}