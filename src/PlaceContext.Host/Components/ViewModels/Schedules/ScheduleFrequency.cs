using PlaceContext.Application;
using PlaceContext.Application.Dtos;
using PlaceContext.Application.Features;
using PlaceContext.Application.Ports;
using PlaceContext.Domain.ValueObjects;

namespace PlaceContext.Host.Components.ViewModels;

public enum ScheduleFrequency
{
    Hour,
    Day,
    Weekday,
    Week,
    Month,
}
