namespace RetailPOS.API.Exceptions
{
    public class ResourceInUseException : Exception
    {
        public List<ResourceReference> References { get; }

        public ResourceInUseException(string message, List<ResourceReference> references)
            : base(message)
        {
            References = references;
        }
    }

    public class ResourceReference
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
