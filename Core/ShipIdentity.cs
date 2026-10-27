using System;
using Cosmoteer.Ships;
using Cosmoteer.Ships.Paint;

namespace ZTX.HingesPistonsDocking
{
    internal sealed class ShipIdentity
    {
        public string Name;
        public string Author;
        public string Description;
        public PaintScheme Paint;

        public static ShipIdentity Capture(Ship ship)
        {
            if (ship?.Metadata == null) return null;
            try
            {
                return new ShipIdentity
                {
                    Name = ship.Metadata.PlainShipName,
                    Author = ship.Metadata.Author,
                    Description = ship.Metadata.Description,
                    Paint = ship.Metadata.GetPaintScheme(),
                };
            }
            catch (Exception ex)
            {
                Log.Exception("ShipIdentity.Capture", ex);
                return null;
            }
        }

        public void ApplyTo(Ship ship)
        {
            if (ship?.Metadata == null) return;
            try
            {
                if (!string.IsNullOrEmpty(Name) && ShipMetadata.IsValidShipName(Name))
                    ship.Metadata.PlainShipName = Name;
                if (Author != null) ship.Metadata.Author = Author;
                if (Description != null) ship.Metadata.Description = Description;
                if (Paint != null) ship.Metadata.SetPaintScheme(Paint);
            }
            catch (Exception ex)
            {
                Log.Exception("ShipIdentity.ApplyTo", ex);
            }
        }
    }
}
