using Host.Library.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Host.Library.Services
{
    public abstract class TwowayOutboundService : OutboundService, ITwowayOutboundService
    {
        public virtual SvgImage SvgImage
        {
            get
            {
                return new SvgImage
                {
                    Paths = new List<SvgPath> {

                        new SvgPath{
                            Path = "M 75.644058,76.254234 A 23.728813,23.728813 0 0 1 51.915245,99.983047 23.728813,23.728813 0 0 1 28.186432,76.254234 23.728813,23.728813 0 0 1 51.915245,52.525421 23.728813,23.728813 0 0 1 75.644058,76.254234 Z",
                            Stroke = "#000000",
                            StrokeWidth = 1M,
                            Fill = "#ffffffff",
                            Opacity = 1M
                        },
                        new SvgPath{
                            Path = "m 42.796614,57.203391 0,36.440678 25.84746,-18.644067 z",
                            Stroke = "#000000",
                            StrokeWidth = 1M,
                            Fill = "#ffc000",
                            Opacity = 1M
                        },
                    }
                };
            }
        }
        public override ServiceConfig GetAdapterConfigMetadata()
        {
            var actionConfig = base.GetAdapterConfigMetadata();
            var routingConfig = new ConfigProperty
            {
                id = "routingExpression",
                name = "Routing expression",
                description = "Script",
                type = "script",
                visibility = "hidden",
                value = @"// This expression is evaluated on the 'route' variable.

var route = true;"
            };
            actionConfig.config.staticConfig.Add(routingConfig);
            return actionConfig;
        }
    }
}
