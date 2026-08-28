using System.Collections.Generic;

namespace PrismStore.Models
{
    public class RootObject
    {
        public List<AppItem> PrismStore { get; set; }
        public List<AppItem> NeonStore { get; set; }
    }
}