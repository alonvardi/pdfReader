using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pdfReader
{
    public class TextAnnotation
    {
        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("boundingPoly")]
        public BoundingPoly BoundingPoly { get; set; }
    }

    public class BoundingPoly
    {
        [JsonProperty("vertices")]
        public List<Vertex> Vertices { get; set; }
    }

    public class Vertex
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public class ApiResponseItem
    {
        [JsonProperty("textAnnotations")]
        public List<TextAnnotation> TextAnnotations { get; set; }
    }

    public class GoogleOCRResponse
    {
        [JsonProperty("responses")]
        public List<ApiResponseItem> Responses { get; set; }
    }

}
