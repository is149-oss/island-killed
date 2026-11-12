using System.Linq;
using System.Windows.Media;

namespace Island.Pets;

internal static class PetArt
{
	private sealed record Skin(string Fur, string Ol, string Cream, string Inner, string Stripe, string Line, string ELine, string Whisk, string Blush, string Nose);

	public const string Black = "black";

	public const string Ginger = "ginger";

	public const string Block = "block";

	private static readonly (string Name, string Svg)[] Icons = new (string, string)[16]
	{
		("mail", "<rect x=\"188\" y=\"22\" width=\"24\" height=\"18\" rx=\"3\" fill=\"#FFD166\" class=\"ol\"/><path d=\"M189 24 L200 33 L211 24\" class=\"ln\"/>"),
		("phone", "<rect x=\"193\" y=\"18\" width=\"14\" height=\"26\" rx=\"3.5\" fill=\"#4CC9F0\" class=\"ol\"/><circle cx=\"200\" cy=\"39.5\" r=\"1.6\" fill=\"#2b2238\"/><path d=\"M198 21.5 h4\" class=\"ln\"/>"),
		("caps", "<text x=\"200\" y=\"41\" text-anchor=\"middle\" font-size=\"27\" font-weight=\"800\" fill=\"#2b2238\">A</text>"),
		("vol", "<path d=\"M187 27 h5 l7 -6 v20 l-7 -6 h-5 z\" fill=\"#2b2238\"/><path d=\"M203 26 q4 5 0 10 M207 22 q8 9 0 18\" class=\"ln\"/>"),
		("mute", "<path d=\"M187 27 h5 l7 -6 v20 l-7 -6 h-5 z\" fill=\"#2b2238\"/><path d=\"M204 26 l9 10 M213 26 l-9 10\" stroke=\"#ff4d6d\" stroke-width=\"2.6\" stroke-linecap=\"round\" fill=\"none\"/>"),
		("down", "<path d=\"M200 18 v15 M193 27 l7 7 l7 -7\" stroke=\"#2fa866\" stroke-width=\"3.4\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/><path d=\"M189 42 h22\" class=\"ln\"/>"),
		("check", "<circle cx=\"200\" cy=\"31\" r=\"12\" fill=\"#3BB273\"/><path d=\"M194 31 l4.5 4.5 l8 -9\" stroke=\"#fff\" stroke-width=\"3.2\" fill=\"none\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>"),
		("bolt", "<path d=\"M204 16 L191 33 H199 L196 46 L210 28 H202 Z\" fill=\"#FFD166\" class=\"ol\"/>"),
		("clock", "<g class=\"ring\"><circle cx=\"191\" cy=\"21\" r=\"4\" fill=\"#FF6B6B\" class=\"ol\"/><circle cx=\"209\" cy=\"21\" r=\"4\" fill=\"#FF6B6B\" class=\"ol\"/><circle cx=\"200\" cy=\"32\" r=\"11\" fill=\"#FF6B6B\" class=\"ol\"/><circle cx=\"200\" cy=\"32\" r=\"7.5\" fill=\"#fff\"/><path d=\"M200 32 v-5 M200 32 h4\" class=\"ln\"/></g>"),
		("gift", "<rect x=\"189\" y=\"28\" width=\"22\" height=\"15\" rx=\"2\" fill=\"#FF6B9A\" class=\"ol\"/><rect x=\"187\" y=\"22\" width=\"26\" height=\"7\" rx=\"2\" fill=\"#FF92B6\" class=\"ol\"/><path d=\"M200 22 v21\" stroke=\"#FFD166\" stroke-width=\"3.5\" fill=\"none\"/><path d=\"M200 22 q-8 -9 -10 -2 q2 3 10 2 q8 -9 10 -2 q-2 3 -10 2\" fill=\"#FFD166\" class=\"ol\"/>"),
		("dots", "<circle class=\"d d1\" cx=\"189\" cy=\"32\" r=\"3.6\" fill=\"#2b2238\"/><circle class=\"d d2\" cx=\"200\" cy=\"32\" r=\"3.6\" fill=\"#2b2238\"/><circle class=\"d d3\" cx=\"211\" cy=\"32\" r=\"3.6\" fill=\"#2b2238\"/>"),
		("wifi", "<path d=\"M187 27 q13 -11 26 0 M191.5 32 q8.5 -7 17 0\" class=\"ln\" stroke-width=\"2.8\"/><circle cx=\"200\" cy=\"37.5\" r=\"2.4\" fill=\"#2b2238\"/>"),
		("wifioff", "<path d=\"M187 27 q13 -11 26 0 M191.5 32 q8.5 -7 17 0\" class=\"ln\" stroke-width=\"2.8\"/><circle cx=\"200\" cy=\"37.5\" r=\"2.4\" fill=\"#2b2238\"/><path d=\"M189 19 L211 43\" stroke=\"#ff4d6d\" stroke-width=\"3\" stroke-linecap=\"round\" fill=\"none\"/>"),
		("bt", "<path d=\"M194.5 24.5 L206 35 L200 41 V20 L206 26 L194.5 37\" fill=\"none\" stroke=\"#3a86ff\" stroke-width=\"2.8\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>"),
		("battery", "<rect x=\"186\" y=\"24\" width=\"24\" height=\"14\" rx=\"3\" fill=\"none\" class=\"ol\" stroke-width=\"2.4\"/><rect x=\"210.5\" y=\"28\" width=\"3\" height=\"6\" rx=\"1\" fill=\"#2b2238\"/><rect x=\"189\" y=\"27\" width=\"5\" height=\"8\" rx=\"1\" fill=\"#ff4d4d\"/>"),
		("file", "<path d=\"M191 19 h12 l6 6 v19 h-18 z\" fill=\"#fff\" class=\"ol\"/><path d=\"M203 19 v6 h6 M195 31 h10 M195 36 h10\" class=\"ln\"/>")
	};

	public static readonly string[] IconNames = Icons.Select(((string Name, string Svg) i) => i.Name).ToArray();

	private const string Block_ = "\n    <g class=\"bar\"><rect class=\"bar-bg\" x=\"82\" y=\"225\" width=\"76\" height=\"9\" rx=\"4.5\"/><rect class=\"bar-fg\" x=\"82\" y=\"225\" width=\"46\" height=\"9\" rx=\"4.5\"/></g>\n    <g class=\"paper\"><rect x=\"146\" y=\"184\" width=\"22\" height=\"27\" rx=\"3\" fill=\"#fff\" stroke=\"#2b2238\" stroke-width=\"2\"/><path d=\"M151 192 h12 M151 197 h12 M151 202 h8\" stroke=\"#9aa4b8\" stroke-width=\"2\" stroke-linecap=\"round\" fill=\"none\"/></g>\n    <g class=\"paw paw-l\"><rect class=\"fur\" x=\"72\" y=\"168\" width=\"13\" height=\"46\" rx=\"2.5\"/><rect class=\"fur\" x=\"94\" y=\"168\" width=\"13\" height=\"46\" rx=\"2.5\"/></g>\n    <g class=\"paw paw-r\"><rect class=\"fur\" x=\"133\" y=\"168\" width=\"13\" height=\"46\" rx=\"2.5\"/><rect class=\"fur\" x=\"155\" y=\"168\" width=\"13\" height=\"46\" rx=\"2.5\"/></g>\n    <g class=\"head\"><g class=\"look\">\n      <g class=\"ear ear-l\"><rect class=\"fur\" x=\"42\" y=\"117\" width=\"24\" height=\"24\" rx=\"3.5\"/></g>\n      <g class=\"ear ear-r\"><rect class=\"fur\" x=\"174\" y=\"117\" width=\"24\" height=\"24\" rx=\"3.5\"/></g>\n      <g class=\"body\">\n        <rect class=\"fur\" x=\"60\" y=\"88\" width=\"120\" height=\"92\" rx=\"8\"/>\n        <rect class=\"b-shade\" x=\"62.5\" y=\"158\" width=\"115\" height=\"19.5\" rx=\"5\"/>\n        <rect class=\"b-hi\" x=\"67\" y=\"92.5\" width=\"106\" height=\"7\" rx=\"3.5\"/>\n      </g>\n      <ellipse class=\"blush\" cx=\"80\" cy=\"147\" rx=\"7\" ry=\"4\"/><ellipse class=\"blush\" cx=\"160\" cy=\"147\" rx=\"7\" ry=\"4\"/>\n      <g class=\"eyes-open\">\n        <g class=\"eye eye-l\"><g class=\"iris\"><path class=\"chev\" d=\"M89 118 L105 128 L89 138\"/></g></g>\n        <g class=\"eye eye-r\"><g class=\"iris\"><path class=\"chev\" d=\"M151 118 L135 128 L151 138\"/></g></g>\n      </g>\n      <g class=\"lids\"><path class=\"lidfill\" d=\"M83 127 H111 V108 H83 Z M129 127 H157 V108 H129 Z\"/><path class=\"line\" d=\"M85 127 h24 M131 127 h24\"/></g>\n      <g class=\"eyes-happy\"><path class=\"eline\" d=\"M88 134 L97 123 L106 134 M134 134 L143 123 L152 134\"/></g>\n      <g class=\"eyes-closed\"><path class=\"eline\" d=\"M88 129 h18 M134 129 h18\"/></g>\n      <rect class=\"mouth-open\" x=\"113\" y=\"141\" width=\"14\" height=\"11\" rx=\"3.5\" fill=\"#3b1a10\" stroke=\"#6e3416\" stroke-width=\"1.5\"/>\n      <path class=\"sweat\" d=\"M196 90 Q189 103 196 107 Q203 103 196 90 Z\" fill=\"#9ad8ff\" stroke=\"#3b7fb0\" stroke-width=\"1.5\"/>\n      <g class=\"phones\">\n        <path d=\"M66 104 Q66 66 120 64 Q174 66 174 104\" fill=\"none\" stroke=\"#2a2a35\" stroke-width=\"7\" stroke-linecap=\"round\"/>\n        <rect x=\"52\" y=\"96\" width=\"17\" height=\"31\" rx=\"7\" fill=\"#2f2f3c\" stroke=\"#15151c\" stroke-width=\"2\"/>\n        <rect x=\"171\" y=\"96\" width=\"17\" height=\"31\" rx=\"7\" fill=\"#2f2f3c\" stroke=\"#15151c\" stroke-width=\"2\"/>\n        <rect x=\"56\" y=\"102\" width=\"4\" height=\"19\" rx=\"2\" fill=\"#8b6cff\"/><rect x=\"180\" y=\"102\" width=\"4\" height=\"19\" rx=\"2\" fill=\"#8b6cff\"/>\n      </g>\n    </g></g>";

	private const string Cat_ = "\n    <g class=\"tail tail-left\">\n      <path class=\"tail-o\" d=\"M86 200 C44 204 34 168 50 136 C54 128 60 126 62 132\"/>\n      <path class=\"tail-f\" d=\"M86 200 C44 204 34 168 50 136 C54 128 60 126 62 132\"/>\n    </g>\n    <g class=\"tail tail-right\">\n      <path class=\"tail-o\" d=\"M155 195 C192 198 202 160 188 132\"/>\n      <path class=\"tail-f\" d=\"M155 195 C192 198 202 160 188 132\"/>\n      <path class=\"tail-s\" d=\"M155 195 C192 198 202 160 188 132\"/>\n    </g>\n    <g class=\"body\">\n      <ellipse class=\"fur\" cx=\"120\" cy=\"180\" rx=\"44\" ry=\"36\"/>\n      <ellipse class=\"cream\" cx=\"120\" cy=\"188\" rx=\"24\" ry=\"22\"/>\n      <path class=\"stripe\" d=\"M83 168 q7 4 8 12 M80 182 q7 3 8 10 M157 168 q-7 4 -8 12 M160 182 q-7 3 -8 10\"/>\n      <path class=\"legs\" d=\"M111 192 q-2 11 0 20 M129 192 q2 11 0 20 M96 186 q-6 12 -4 24 M144 186 q6 12 4 24\"/>\n    </g>\n    <g class=\"bar\"><rect class=\"bar-bg\" x=\"82\" y=\"225\" width=\"76\" height=\"9\" rx=\"4.5\"/><rect class=\"bar-fg\" x=\"82\" y=\"225\" width=\"46\" height=\"9\" rx=\"4.5\"/></g>\n    <g class=\"paper\"><rect x=\"146\" y=\"184\" width=\"22\" height=\"27\" rx=\"3\" fill=\"#fff\" stroke=\"#2b2238\" stroke-width=\"2\"/><path d=\"M151 192 h12 M151 197 h12 M151 202 h8\" stroke=\"#9aa4b8\" stroke-width=\"2\" stroke-linecap=\"round\" fill=\"none\"/></g>\n    <g class=\"paw paw-l\"><ellipse class=\"fur\" cx=\"104\" cy=\"212\" rx=\"12\" ry=\"9\"/><path class=\"line\" style=\"stroke-width:2\" d=\"M100 214 v4 M108 214 v4\"/></g>\n    <g class=\"paw paw-r\"><ellipse class=\"fur\" cx=\"136\" cy=\"212\" rx=\"12\" ry=\"9\"/><path class=\"line\" style=\"stroke-width:2\" d=\"M132 214 v4 M140 214 v4\"/></g>\n    <g class=\"head\"><g class=\"look\">\n      <g class=\"ear ear-l\"><path class=\"fur\" d=\"M78 96 L74 48 L114 74 Z\"/><path class=\"inner\" d=\"M84 86 L81 60 L104 76 Z\"/></g>\n      <g class=\"ear ear-r\"><path class=\"fur\" d=\"M162 96 L166 48 L126 74 Z\"/><path class=\"inner\" d=\"M156 86 L159 60 L136 76 Z\"/></g>\n      <ellipse class=\"fur\" cx=\"120\" cy=\"112\" rx=\"54\" ry=\"44\"/>\n      <path class=\"stripe\" d=\"M120 72 v12 M110 74 l2 9 M130 74 l-2 9\"/>\n      <ellipse class=\"sheen\" cx=\"104\" cy=\"84\" rx=\"24\" ry=\"9\" transform=\"rotate(-12 104 84)\"/>\n      <ellipse class=\"cream\" cx=\"120\" cy=\"131\" rx=\"20\" ry=\"13\"/>\n      <ellipse class=\"blush\" cx=\"86\" cy=\"131\" rx=\"7\" ry=\"4\"/><ellipse class=\"blush\" cx=\"154\" cy=\"131\" rx=\"7\" ry=\"4\"/>\n      <path class=\"whisker\" d=\"M82 126 L58 122 M82 132 L58 134 M158 126 L182 122 M158 132 L182 134\"/>\n      <g class=\"eyes-open\">\n        <g class=\"eye eye-l\"><ellipse class=\"sclera\" cx=\"98\" cy=\"116\" rx=\"13.5\" ry=\"14.5\"/><g class=\"iris\"><ellipse class=\"pupil\" cx=\"98\" cy=\"116\" rx=\"10\" ry=\"12\"/><circle class=\"hl\" cx=\"101.5\" cy=\"111\" r=\"3.6\"/><circle class=\"hl\" cx=\"95\" cy=\"120.5\" r=\"1.7\"/></g></g>\n        <g class=\"eye eye-r\"><ellipse class=\"sclera\" cx=\"142\" cy=\"116\" rx=\"13.5\" ry=\"14.5\"/><g class=\"iris\"><ellipse class=\"pupil\" cx=\"142\" cy=\"116\" rx=\"10\" ry=\"12\"/><circle class=\"hl\" cx=\"145.5\" cy=\"111\" r=\"3.6\"/><circle class=\"hl\" cx=\"139\" cy=\"120.5\" r=\"1.7\"/></g></g>\n      </g>\n      <g class=\"lids\"><path class=\"lidfill\" d=\"M83 116 L113 116 L113 99 L83 99 Z M127 116 L157 116 L157 99 L127 99 Z\"/><path class=\"line\" d=\"M85 116 Q98 118.5 111 116 M129 116 Q142 118.5 155 116\"/></g>\n      <g class=\"eyes-happy\"><path class=\"eline\" d=\"M89 119 Q98 107 107 119 M133 119 Q142 107 151 119\"/></g>\n      <g class=\"eyes-closed\"><path class=\"eline\" d=\"M89 115 Q98 123 107 115 M133 115 Q142 123 151 115\"/></g>\n      <path class=\"nose\" d=\"M116 124 L124 124 L120 129 Z\"/>\n      <path class=\"line\" d=\"M120 129 Q120 134 114 135 M120 129 Q120 134 126 135\"/>\n      <ellipse class=\"mouth-open\" cx=\"120\" cy=\"137\" rx=\"5\" ry=\"6\" fill=\"#7a2333\" stroke=\"#6B3410\" stroke-width=\"1.5\"/>\n      <path class=\"sweat\" d=\"M172 80 Q165 92 172 96 Q179 92 172 80 Z\" fill=\"#9ad8ff\" stroke=\"#3b7fb0\" stroke-width=\"1.5\"/>\n      <g class=\"phones\">\n        <path d=\"M68 112 Q68 58 120 56 Q172 58 172 112\" fill=\"none\" stroke=\"#2a2a35\" stroke-width=\"7\" stroke-linecap=\"round\"/>\n        <rect x=\"58\" y=\"100\" width=\"17\" height=\"31\" rx=\"7\" fill=\"#2f2f3c\" stroke=\"#15151c\" stroke-width=\"2\"/>\n        <rect x=\"165\" y=\"100\" width=\"17\" height=\"31\" rx=\"7\" fill=\"#2f2f3c\" stroke=\"#15151c\" stroke-width=\"2\"/>\n        <rect x=\"62\" y=\"106\" width=\"4\" height=\"19\" rx=\"2\" fill=\"#8b6cff\"/><rect x=\"174\" y=\"106\" width=\"4\" height=\"19\" rx=\"2\" fill=\"#8b6cff\"/>\n      </g>\n    </g></g>";

	private static readonly Skin GingerSkin = new Skin("#F4A259", "#6B3410", "#FFE6C7", "#FFB4B4", "#D27A2C", "#6B3410", "#2A2238", "#6B3410", "#FF8FA3", "#FF7F98");

	private static readonly Skin BlackSkin = new Skin("#34343c", "#121216", "#3d3d46", "#77777f", "#D27A2C", "#9a9aa5", "#d8d8e0", "#d8d8e0", "#FF8FA3", "#111115");

	private static readonly Skin BlockSkin = GingerSkin with
	{
		Fur = "#DD8752",
		Ol = "#6e3416",
		Line = "#6e3416",
		ELine = "#1b1b1d",
		Blush = "#ff8fa3"
	};

	public static string Markup(string kind)
	{
		return $"\n  <ellipse class=\"shadow\" cx=\"120\" cy=\"223\" rx=\"50\" ry=\"6\"/>\n  <g class=\"cloud\">\n    <g class=\"drops\" stroke=\"#7fc4ff\" stroke-width=\"2.4\" stroke-linecap=\"round\"><line x1=\"108\" y1=\"38\" x2=\"106\" y2=\"44\"/><line x1=\"121\" y1=\"40\" x2=\"119\" y2=\"46\"/><line x1=\"134\" y1=\"38\" x2=\"132\" y2=\"44\"/></g>\n    <g fill=\"#9aa4b8\" stroke=\"#5d6578\" stroke-width=\"2\"><circle cx=\"105\" cy=\"27\" r=\"10\"/><circle cx=\"121\" cy=\"21\" r=\"14\"/><circle cx=\"137\" cy=\"27\" r=\"10\"/></g>\n    <rect x=\"102\" y=\"25\" width=\"38\" height=\"12\" rx=\"6\" fill=\"#9aa4b8\"/>\n  </g>\n  <g class=\"cat\">{((kind == "block") ? "\n    <g class=\"bar\"><rect class=\"bar-bg\" x=\"82\" y=\"225\" width=\"76\" height=\"9\" rx=\"4.5\"/><rect class=\"bar-fg\" x=\"82\" y=\"225\" width=\"46\" height=\"9\" rx=\"4.5\"/></g>\n    <g class=\"paper\"><rect x=\"146\" y=\"184\" width=\"22\" height=\"27\" rx=\"3\" fill=\"#fff\" stroke=\"#2b2238\" stroke-width=\"2\"/><path d=\"M151 192 h12 M151 197 h12 M151 202 h8\" stroke=\"#9aa4b8\" stroke-width=\"2\" stroke-linecap=\"round\" fill=\"none\"/></g>\n    <g class=\"paw paw-l\"><rect class=\"fur\" x=\"72\" y=\"168\" width=\"13\" height=\"46\" rx=\"2.5\"/><rect class=\"fur\" x=\"94\" y=\"168\" width=\"13\" height=\"46\" rx=\"2.5\"/></g>\n    <g class=\"paw paw-r\"><rect class=\"fur\" x=\"133\" y=\"168\" width=\"13\" height=\"46\" rx=\"2.5\"/><rect class=\"fur\" x=\"155\" y=\"168\" width=\"13\" height=\"46\" rx=\"2.5\"/></g>\n    <g class=\"head\"><g class=\"look\">\n      <g class=\"ear ear-l\"><rect class=\"fur\" x=\"42\" y=\"117\" width=\"24\" height=\"24\" rx=\"3.5\"/></g>\n      <g class=\"ear ear-r\"><rect class=\"fur\" x=\"174\" y=\"117\" width=\"24\" height=\"24\" rx=\"3.5\"/></g>\n      <g class=\"body\">\n        <rect class=\"fur\" x=\"60\" y=\"88\" width=\"120\" height=\"92\" rx=\"8\"/>\n        <rect class=\"b-shade\" x=\"62.5\" y=\"158\" width=\"115\" height=\"19.5\" rx=\"5\"/>\n        <rect class=\"b-hi\" x=\"67\" y=\"92.5\" width=\"106\" height=\"7\" rx=\"3.5\"/>\n      </g>\n      <ellipse class=\"blush\" cx=\"80\" cy=\"147\" rx=\"7\" ry=\"4\"/><ellipse class=\"blush\" cx=\"160\" cy=\"147\" rx=\"7\" ry=\"4\"/>\n      <g class=\"eyes-open\">\n        <g class=\"eye eye-l\"><g class=\"iris\"><path class=\"chev\" d=\"M89 118 L105 128 L89 138\"/></g></g>\n        <g class=\"eye eye-r\"><g class=\"iris\"><path class=\"chev\" d=\"M151 118 L135 128 L151 138\"/></g></g>\n      </g>\n      <g class=\"lids\"><path class=\"lidfill\" d=\"M83 127 H111 V108 H83 Z M129 127 H157 V108 H129 Z\"/><path class=\"line\" d=\"M85 127 h24 M131 127 h24\"/></g>\n      <g class=\"eyes-happy\"><path class=\"eline\" d=\"M88 134 L97 123 L106 134 M134 134 L143 123 L152 134\"/></g>\n      <g class=\"eyes-closed\"><path class=\"eline\" d=\"M88 129 h18 M134 129 h18\"/></g>\n      <rect class=\"mouth-open\" x=\"113\" y=\"141\" width=\"14\" height=\"11\" rx=\"3.5\" fill=\"#3b1a10\" stroke=\"#6e3416\" stroke-width=\"1.5\"/>\n      <path class=\"sweat\" d=\"M196 90 Q189 103 196 107 Q203 103 196 90 Z\" fill=\"#9ad8ff\" stroke=\"#3b7fb0\" stroke-width=\"1.5\"/>\n      <g class=\"phones\">\n        <path d=\"M66 104 Q66 66 120 64 Q174 66 174 104\" fill=\"none\" stroke=\"#2a2a35\" stroke-width=\"7\" stroke-linecap=\"round\"/>\n        <rect x=\"52\" y=\"96\" width=\"17\" height=\"31\" rx=\"7\" fill=\"#2f2f3c\" stroke=\"#15151c\" stroke-width=\"2\"/>\n        <rect x=\"171\" y=\"96\" width=\"17\" height=\"31\" rx=\"7\" fill=\"#2f2f3c\" stroke=\"#15151c\" stroke-width=\"2\"/>\n        <rect x=\"56\" y=\"102\" width=\"4\" height=\"19\" rx=\"2\" fill=\"#8b6cff\"/><rect x=\"180\" y=\"102\" width=\"4\" height=\"19\" rx=\"2\" fill=\"#8b6cff\"/>\n      </g>\n    </g></g>" : "\n    <g class=\"tail tail-left\">\n      <path class=\"tail-o\" d=\"M86 200 C44 204 34 168 50 136 C54 128 60 126 62 132\"/>\n      <path class=\"tail-f\" d=\"M86 200 C44 204 34 168 50 136 C54 128 60 126 62 132\"/>\n    </g>\n    <g class=\"tail tail-right\">\n      <path class=\"tail-o\" d=\"M155 195 C192 198 202 160 188 132\"/>\n      <path class=\"tail-f\" d=\"M155 195 C192 198 202 160 188 132\"/>\n      <path class=\"tail-s\" d=\"M155 195 C192 198 202 160 188 132\"/>\n    </g>\n    <g class=\"body\">\n      <ellipse class=\"fur\" cx=\"120\" cy=\"180\" rx=\"44\" ry=\"36\"/>\n      <ellipse class=\"cream\" cx=\"120\" cy=\"188\" rx=\"24\" ry=\"22\"/>\n      <path class=\"stripe\" d=\"M83 168 q7 4 8 12 M80 182 q7 3 8 10 M157 168 q-7 4 -8 12 M160 182 q-7 3 -8 10\"/>\n      <path class=\"legs\" d=\"M111 192 q-2 11 0 20 M129 192 q2 11 0 20 M96 186 q-6 12 -4 24 M144 186 q6 12 4 24\"/>\n    </g>\n    <g class=\"bar\"><rect class=\"bar-bg\" x=\"82\" y=\"225\" width=\"76\" height=\"9\" rx=\"4.5\"/><rect class=\"bar-fg\" x=\"82\" y=\"225\" width=\"46\" height=\"9\" rx=\"4.5\"/></g>\n    <g class=\"paper\"><rect x=\"146\" y=\"184\" width=\"22\" height=\"27\" rx=\"3\" fill=\"#fff\" stroke=\"#2b2238\" stroke-width=\"2\"/><path d=\"M151 192 h12 M151 197 h12 M151 202 h8\" stroke=\"#9aa4b8\" stroke-width=\"2\" stroke-linecap=\"round\" fill=\"none\"/></g>\n    <g class=\"paw paw-l\"><ellipse class=\"fur\" cx=\"104\" cy=\"212\" rx=\"12\" ry=\"9\"/><path class=\"line\" style=\"stroke-width:2\" d=\"M100 214 v4 M108 214 v4\"/></g>\n    <g class=\"paw paw-r\"><ellipse class=\"fur\" cx=\"136\" cy=\"212\" rx=\"12\" ry=\"9\"/><path class=\"line\" style=\"stroke-width:2\" d=\"M132 214 v4 M140 214 v4\"/></g>\n    <g class=\"head\"><g class=\"look\">\n      <g class=\"ear ear-l\"><path class=\"fur\" d=\"M78 96 L74 48 L114 74 Z\"/><path class=\"inner\" d=\"M84 86 L81 60 L104 76 Z\"/></g>\n      <g class=\"ear ear-r\"><path class=\"fur\" d=\"M162 96 L166 48 L126 74 Z\"/><path class=\"inner\" d=\"M156 86 L159 60 L136 76 Z\"/></g>\n      <ellipse class=\"fur\" cx=\"120\" cy=\"112\" rx=\"54\" ry=\"44\"/>\n      <path class=\"stripe\" d=\"M120 72 v12 M110 74 l2 9 M130 74 l-2 9\"/>\n      <ellipse class=\"sheen\" cx=\"104\" cy=\"84\" rx=\"24\" ry=\"9\" transform=\"rotate(-12 104 84)\"/>\n      <ellipse class=\"cream\" cx=\"120\" cy=\"131\" rx=\"20\" ry=\"13\"/>\n      <ellipse class=\"blush\" cx=\"86\" cy=\"131\" rx=\"7\" ry=\"4\"/><ellipse class=\"blush\" cx=\"154\" cy=\"131\" rx=\"7\" ry=\"4\"/>\n      <path class=\"whisker\" d=\"M82 126 L58 122 M82 132 L58 134 M158 126 L182 122 M158 132 L182 134\"/>\n      <g class=\"eyes-open\">\n        <g class=\"eye eye-l\"><ellipse class=\"sclera\" cx=\"98\" cy=\"116\" rx=\"13.5\" ry=\"14.5\"/><g class=\"iris\"><ellipse class=\"pupil\" cx=\"98\" cy=\"116\" rx=\"10\" ry=\"12\"/><circle class=\"hl\" cx=\"101.5\" cy=\"111\" r=\"3.6\"/><circle class=\"hl\" cx=\"95\" cy=\"120.5\" r=\"1.7\"/></g></g>\n        <g class=\"eye eye-r\"><ellipse class=\"sclera\" cx=\"142\" cy=\"116\" rx=\"13.5\" ry=\"14.5\"/><g class=\"iris\"><ellipse class=\"pupil\" cx=\"142\" cy=\"116\" rx=\"10\" ry=\"12\"/><circle class=\"hl\" cx=\"145.5\" cy=\"111\" r=\"3.6\"/><circle class=\"hl\" cx=\"139\" cy=\"120.5\" r=\"1.7\"/></g></g>\n      </g>\n      <g class=\"lids\"><path class=\"lidfill\" d=\"M83 116 L113 116 L113 99 L83 99 Z M127 116 L157 116 L157 99 L127 99 Z\"/><path class=\"line\" d=\"M85 116 Q98 118.5 111 116 M129 116 Q142 118.5 155 116\"/></g>\n      <g class=\"eyes-happy\"><path class=\"eline\" d=\"M89 119 Q98 107 107 119 M133 119 Q142 107 151 119\"/></g>\n      <g class=\"eyes-closed\"><path class=\"eline\" d=\"M89 115 Q98 123 107 115 M133 115 Q142 123 151 115\"/></g>\n      <path class=\"nose\" d=\"M116 124 L124 124 L120 129 Z\"/>\n      <path class=\"line\" d=\"M120 129 Q120 134 114 135 M120 129 Q120 134 126 135\"/>\n      <ellipse class=\"mouth-open\" cx=\"120\" cy=\"137\" rx=\"5\" ry=\"6\" fill=\"#7a2333\" stroke=\"#6B3410\" stroke-width=\"1.5\"/>\n      <path class=\"sweat\" d=\"M172 80 Q165 92 172 96 Q179 92 172 80 Z\" fill=\"#9ad8ff\" stroke=\"#3b7fb0\" stroke-width=\"1.5\"/>\n      <g class=\"phones\">\n        <path d=\"M68 112 Q68 58 120 56 Q172 58 172 112\" fill=\"none\" stroke=\"#2a2a35\" stroke-width=\"7\" stroke-linecap=\"round\"/>\n        <rect x=\"58\" y=\"100\" width=\"17\" height=\"31\" rx=\"7\" fill=\"#2f2f3c\" stroke=\"#15151c\" stroke-width=\"2\"/>\n        <rect x=\"165\" y=\"100\" width=\"17\" height=\"31\" rx=\"7\" fill=\"#2f2f3c\" stroke=\"#15151c\" stroke-width=\"2\"/>\n        <rect x=\"62\" y=\"106\" width=\"4\" height=\"19\" rx=\"2\" fill=\"#8b6cff\"/><rect x=\"174\" y=\"106\" width=\"4\" height=\"19\" rx=\"2\" fill=\"#8b6cff\"/>\n      </g>\n    </g></g>")}</g>\n  <g class=\"bubble\"><path class=\"bub\" d=\"M182 8 H218 A12 12 0 0 1 230 20 V42 A12 12 0 0 1 218 54 H192 L178 66 L182 54 A12 12 0 0 1 170 42 V20 A12 12 0 0 1 182 8 Z\"/>{string.Concat(Icons.Select(((string Name, string Svg) i) => $"<g class=\"ic ic-{i.Name}\">{i.Svg}</g>"))}</g>\n  <g class=\"fx\"/>";
	}

	public static Color FurColor(string kind)
	{
		return (Color)ColorConverter.ConvertFromString(SkinOf(kind).Fur);
	}

	public static Color OutlineColor(string kind)
	{
		return (Color)ColorConverter.ConvertFromString(SkinOf(kind).Ol);
	}

	public static Color LineColor(string kind)
	{
		return (Color)ColorConverter.ConvertFromString(SkinOf(kind).Line);
	}

	private static Skin SkinOf(string kind)
	{
		if (!(kind == "black"))
		{
			if (!(kind == "block"))
			{
				return GingerSkin;
			}
			return BlockSkin;
		}
		return BlackSkin;
	}

	public static MiniSvg.Paint Painter(string kind)
	{
		Skin k = SkinOf(kind);
		bool black = kind == "black";
		return (string cls, MiniSvg.Style s) =>
		{
			switch (cls)
			{
			case "fur":
				Fill(k.Fur);
				Stroke(k.Ol, 3.0);
				s.Join = PenLineJoin.Round;
				break;
			case "lidfill":
				Fill(k.Fur);
				break;
			case "cream":
				Fill(k.Cream);
				break;
			case "inner":
				Fill(k.Inner);
				break;
			case "stripe":
				Fill(null);
				Stroke(k.Stripe, 4.0);
				Round();
				break;
			case "pupil":
				Fill(black ? "#101014" : "#2A2238");
				break;
			case "hl":
				Fill("#fff");
				break;
			case "line":
				Fill(null);
				Stroke(k.Line, 2.6);
				Round();
				break;
			case "eline":
				Fill(null);
				Stroke(k.ELine, 3.6);
				Round();
				break;
			case "whisker":
				Fill(null);
				Stroke(k.Whisk, 1.6);
				Round();
				s.Opacity = (black ? 0.75 : 0.5);
				break;
			case "blush":
			{
				Fill(k.Blush);
				ref double? opacity = ref s.Opacity;
				double value = (black ? 0.22 : ((kind == "block") ? 0.35 : 0.45));
				opacity = value;
				break;
			}
			case "nose":
				Fill(k.Nose);
				Stroke(k.Ol, 1.5);
				s.Join = PenLineJoin.Round;
				break;
			case "tail-o":
				Fill(null);
				Stroke(k.Ol, 22.0);
				Round();
				break;
			case "tail-f":
				Fill(null);
				Stroke(k.Fur, 16.0);
				Round();
				break;
			case "tail-s":
				Fill(null);
				Stroke(k.Stripe, 16.0);
				s.Dash = new double[2] { 5.0, 13.0 };
				break;
			case "sclera":
				Fill("#cfe78f");
				Stroke("#0d0d10", 2.4);
				break;
			case "legs":
				Fill(null);
				Stroke("#45454f", 2.4);
				Round();
				break;
			case "sheen":
				Fill("#ffffff");
				s.Opacity = 0.07;
				break;
			case "chev":
				Fill(null);
				Stroke("#1b1b1d", 5.5);
				Round();
				break;
			case "b-shade":
				Fill("#000");
				s.Opacity = 0.1;
				break;
			case "b-hi":
				Fill("#fff");
				s.Opacity = 0.2;
				break;
			case "shadow":
				Fill("#000");
				s.Opacity = 0.22;
				break;
			case "bub":
				Fill("#fff");
				Stroke("#2b2238", 2.5);
				s.Join = PenLineJoin.Round;
				break;
			case "ol":
				Stroke("#2b2238", 2.0);
				s.Join = PenLineJoin.Round;
				break;
			case "ln":
				Fill(null);
				Stroke("#2b2238", 2.2);
				Round();
				break;
			case "bar-bg":
				Fill("#00000055");
				Stroke("#6B3410", 2.0);
				break;
			case "bar-fg":
				Fill("#5BD08A");
				break;
			}
			void Fill(string? c)
			{
				s.Fill = ((c == null) ? null : MiniSvg.Color(c));
				s.FillSet = true;
			}
			void Round()
			{
				s.Cap = PenLineCap.Round;
				s.Join = PenLineJoin.Round;
			}
			void Stroke(string c, double w)
			{
				s.Stroke = MiniSvg.Color(c);
				s.StrokeSet = true;
				s.StrokeWidth = w;
			}
		};
	}
}
