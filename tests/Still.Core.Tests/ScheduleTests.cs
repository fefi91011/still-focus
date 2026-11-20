using Still.Core.Models;
using Xunit;

namespace Still.Core.Tests;

public sealed class ScheduleTests
{
    [Fact]
    public void Always_IsActive()
    {
        var schedule = Schedule.Always();
        Assert.True(schedule.IsActiveAt(TestTime.Local(2026, 3, 2, 3, 15)));
        Assert.Equal("Immer", schedule.Describe());
    }

    [Fact]
    public void Weekly_Window_Is_Active_On_Selected_Day()
    {
        var schedule = new Schedule
        {
            Mode = ScheduleMode.Weekly,
            Windows = [new TimeWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0))]
        };

        Assert.True(schedule.IsActiveAt(TestTime.Local(2026, 3, 2, 9, 30)));   // Monday
        Assert.False(schedule.IsActiveAt(TestTime.Local(2026, 3, 2, 18, 0)));  // Monday evening
        Assert.False(schedule.IsActiveAt(TestTime.Local(2026, 3, 3, 10, 0)));  // Tuesday
    }

    [Fact]
    public void Overnight_Window_Covers_The_Following_Morning()
    {
        var schedule = new Schedule
        {
            Mode = ScheduleMode.Weekly,
            Windows = [new TimeWindow(DayOfWeek.Monday, new TimeOnly(22, 0), new TimeOnly(2, 0))]
        };

        Assert.True(schedule.IsActiveAt(TestTime.Local(2026, 3, 2, 23, 0))); // Monday night
        Assert.True(schedule.IsActiveAt(TestTime.Local(2026, 3, 3, 1, 0)));  // Tuesday early
        Assert.False(schedule.IsActiveAt(TestTime.Local(2026, 3, 3, 2, 30)));
        Assert.False(schedule.IsActiveAt(TestTime.Local(2026, 3, 3, 22, 30))); // Tuesday night
    }

    [Fact]
    public void Full_Day_Window_Lasts_Twenty_Four_Hours()
    {
        var window = new TimeWindow(DayOfWeek.Monday, TimeOnly.MinValue, TimeOnly.MinValue);
        Assert.Equal(TimeSpan.FromDays(1), window.Duration);

        var schedule = new Schedule { Mode = ScheduleMode.Weekly, Windows = [window] };
        Assert.True(schedule.IsActiveAt(TestTime.Local(2026, 3, 2, 23, 59)));
        Assert.False(schedule.IsActiveAt(TestTime.Local(2026, 3, 3, 0, 1)));
    }

    [Fact]
    public void NextTransition_Reports_Window_Start_And_End()
    {
        var schedule = new Schedule
        {
            Mode = ScheduleMode.Weekly,
            Windows = [new TimeWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(10, 0))]
        };

        var before = schedule.NextTransition(TestTime.Local(2026, 3, 2, 8, 0));
        Assert.NotNull(before.StartsAt);
        Assert.Equal(new DateTime(2026, 3, 2, 9, 0, 0), before.StartsAt!.Value.LocalDateTime);
        Assert.Null(before.EndsAt);

        var during = schedule.NextTransition(TestTime.Local(2026, 3, 2, 9, 30));
        Assert.NotNull(during.EndsAt);
        Assert.Equal(new DateTime(2026, 3, 2, 10, 0, 0), during.EndsAt!.Value.LocalDateTime);
    }

    [Fact]
    public void Describe_Contains_Weekdays_And_Time()
    {
        var schedule = new Schedule
        {
            Mode = ScheduleMode.Weekly,
            Windows =
            [
                new TimeWindow(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(17, 0)),
                new TimeWindow(DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(17, 0))
            ]
        };

        var text = schedule.Describe();
        Assert.Contains("Mo", text);
        Assert.Contains("Di", text);
        Assert.Contains("09:00", text);
        Assert.Contains("17:00", text);
    }
}
