﻿using System.ComponentModel.DataAnnotations.Schema;
using ActivityArchiveService.Database.Entities.Base;

namespace ActivityArchiveService.Database.Entities;

public class PresenceStatus : BaseEntity
{
    public PresenceStatusDetails Before { get; set; }

    public PresenceStatusDetails After { get; set; }

    public User User { get; set; }
}
