﻿using Domain.Models;

public class NotificationData : BaseEntity
{
    public required string UserId { get; set; }
    public required string Message { get; set; }
    public bool IsRead { get; set; } = false;
}