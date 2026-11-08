using Avalonia;
using Avalonia.Media;
using SpiderPet.Core;

namespace SpiderPet.Sandbox;

/// <summary>Draws the spider entirely with SVG graphics perfectly bound to the IK skeleton.</summary>
internal static class SpiderRenderer
{
    private static readonly IBrush Ink = Brushes.Black;

    // --- BODY & PALP SVGs ---
    private const string SvgCarapace = "m 426.86783,301.93381 c -0.0816,-18.57712 -1.94531,-34.2793 -1.94531,-34.2793 0,0 2.13844,-6.30846 -29.22665,-18.58567 0,0 -0.72699,0.50264 -7.29093,-14.60183 -8.08515,-18.60499 -12.83739,-16.73271 -20.83203,-15.89453 -7.99465,0.8382 -7.32227,20.73633 -7.32227,20.73633 l -0.11914,2.80859 c -3.1092,2.1283 -7.52385,3.39947 -11.82226,-1.49805 l 0.0254,-1.67968 c 0,0 2.53572,-20.45123 -5.39649,-21.75391 -7.93221,-1.30267 -14.1688,-3.24725 -22.42773,15.28125 -6.59662,14.7992 -7.56203,12.82887 -8.11777,13.48564 -32.02727,10.42845 -30.2709,17.5163 -30.2709,17.51631 0,0 -11.10455,62.26625 1.79296,86.0586 11.86205,21.8822 45.04253,33.6809 47.11685,33.7563 l 3.25425,0.2144 c 3.99669,0.27555 9.7168,0.38844 15.46094,0.42383 l 18.01392,0.61302 c 1.52168,-0.24655 2.63061,-0.51341 2.63061,-0.51341 0,0 33.4644,-7.55833 47.72656,-30.5586 7.13108,-11.50013 8.8316,-32.95218 8.75,-51.52929 z";
    private const string SvgAbdomen = "m 453.09968,474.11559 c -0.0428,-7.97297 -0.86374,-16.38265 -2.68511,-25.19438 -14.57101,-70.49384 -79.64603,-53.02611 -81.67966,-57.5432 -0.24945,-0.55406 0.007,-1.52639 -0.89542,-1.9062 l -6.92429,-0.54626 c -10.53906,-0.58473 -28.88763,-0.91797 -29.79738,1.06028 -0.042,0.0913 -0.16218,0.60406 -0.18726,0.72102 -0.0353,0.0852 -0.0697,0.17223 -0.10855,0.25402 -2.12741,4.4737 -66.82353,-14.34762 -82.86178,55.82694 -16.03827,70.17457 32.44816,116.0495 32.44816,116.0495 0,0 35.85298,23.5083 39.59075,32.84843 3.73779,9.34014 5.42991,29.26325 14.75133,25.90607 9.32143,-3.35718 -2.48401,-11.56705 -0.0508,-14.49963 2.43321,-2.93261 11.95642,-1.60502 11.95642,-1.60502 l 1.84846,-0.001 c 0,0 9.47266,-1.08892 11.84417,1.89379 2.37151,2.9827 -9.60106,10.94457 -0.35168,14.49546 9.24936,3.55088 11.35538,-16.33138 15.28717,-25.59151 3.93178,-9.26012 40.26696,-32.01544 40.26696,-32.01544 0,0 37.84843,-34.34171 37.54902,-90.15247 z";
    private const string SvgPalpLeft = "m 294.57416,254.58396 c 0,0 -11.78844,-19.80457 -7.0057,-22.0949 4.78274,-2.29032 -0.0674,0.0674 0.13473,-1.68406 0.20208,-1.75142 -3.40181,-20.00666 1.65038,-20.27611 5.05218,-0.26945 0.20208,-1.11148 1.54933,-4.51328 1.34725,-3.40181 2.12192,-17.7837 5.49005,-18.12051 3.36812,-0.33681 -0.67363,-0.0674 -0.40418,-2.76186 0.26945,-2.6945 4.17648,-30.78466 14.75239,-29.90894 10.57591,0.87571 8.62239,17.64897 8.62239,17.64897 0,0 -4.64801,16.09963 -6.60152,16.30172 -1.95351,0.20208 -0.0674,0.0674 0.20209,1.88615 0.26945,1.81878 -1.21253,8.28558 -5.05219,12.66414 -3.83966,4.37856 -3.63757,8.95921 -6.39943,8.68976 -2.76187,-0.26945 0.47153,0.20209 0.20208,1.95352 -0.26945,1.75142 -1.01043,17.37952 -2.29032,16.5038 -1.27989,-0.87571 1.48197,-0.13472 1.6167,2.08824 0.13472,2.22296 4.51328,17.71633 1.0778,20.88237 -3.43549,3.16604 -7.5446,0.74099 -7.5446,0.74099 z";
    private const string SvgPalpRight = "m 408.62745,257.04045 c 0,0 12.73903,-20.70776 7.57062,-23.10254 -5.16841,-2.39477 0.0728,0.0705 -0.14559,-1.76086 -0.21838,-1.83129 3.67612,-20.91907 -1.78346,-21.20081 -5.45958,-0.28173 -0.21838,-1.16217 -1.67427,-4.71911 -1.45589,-3.55695 -2.29302,-18.59472 -5.93275,-18.94689 -3.63972,-0.35217 0.72795,-0.0705 0.43677,-2.88782 -0.29118,-2.81738 -4.51326,-32.1886 -15.94199,-31.27294 -11.42872,0.91564 -9.31767,18.45385 -9.31767,18.45385 0,0 5.02281,16.83386 7.13385,17.04517 2.11103,0.21129 0.0728,0.0705 -0.21839,1.97216 -0.29118,1.90173 1.31031,8.66345 5.45959,13.24169 4.14928,4.57825 3.93089,9.3678 6.91546,9.08606 2.98458,-0.28174 -0.50955,0.21131 -0.21838,2.04261 0.29118,1.8313 1.09191,18.17212 2.47501,17.25646 1.3831,-0.91565 -1.60147,-0.14086 -1.74707,2.18348 -0.14558,2.32433 -4.87722,18.52428 -1.16471,21.83471 3.71252,3.31043 8.15298,0.77478 8.15298,0.77478 z";

    // --- LEG SVGs ---
    private const string SvgF1_Femur = "m 262.10274,231.72692 c -5.87715,-0.9236 -16.83375,5.19225 -17.53496,6.0328 -0.25862,1.72962 0.53341,4.93788 -0.6289,5.78321 -2.09583,1.52424 9.52534,23.24501 13.33593,23.43554 3.8106,0.19053 13.90931,12.00263 34.10547,-2.28711 11.81285,-18.10033 -13.9082,-30.29297 -13.9082,-30.29297 l -7.8125,0.38086 c -2.73269,-0.78889 -5.43167,-1.61579 -7.55684,-3.05233 z";
    private const string SvgF1_Tibia = "m 236.12083,124.2087 c -1.35071,0.055 -1.65205,0.42489 -1.70724,2.61227 0,0 0.63851,45.91804 5.97657,53.21076 5.33808,7.29271 4.01397,14.95032 4.01397,14.95032 0,0 -7.13273,25.91904 -3.4938,33.82827 1.98202,4.30792 1.92359,9.61988 4.05943,10.19831 7.13311,-1.05768 18.55283,-2.67374 17.47774,-4.88014 l -0.10946,-2.26186 c 0,0 7.2076,-30.30289 6.0247,-31.79651 -1.1829,-1.49362 0.57557,-65.00798 -12.54144,-70.95234 -7.10405,-3.2194 -6.81628,-4.24803 -8.40489,-4.68257 -4.01834,-0.17396 -7.36736,-0.38666 -11.29557,-0.22647 z";
    private const string SvgF1_Tarsus = "m 234.12408,54.261221 c -9.497,-0.198031 -1.01623,71.122709 0.36103,72.627319 2.0139,2.58088 9.83566,0.48782 13.87039,0.20414 0.88768,-0.50918 2.44458,-0.72353 3.96389,-3.41154 2.47688,-4.38219 -7.24157,-66.684142 -17.72071,-69.35156 -0.16373,-0.04168 -0.32089,-0.06515 -0.4746,-0.06836 z";

    private const string SvgF2_Femur = "m 258.95635,269.06147 c -2.27111,0.0645 -4.66653,0.40329 -7.17578,1.09375 -12.39196,3.40987 -14.96147,8.24085 -19.65039,8.65039 -10.54588,1.51355 -7.61928,9.54422 -5.50586,12.80274 8.90542,9.27679 16.45845,9.86535 26.49609,10.91406 12.7655,1.33371 28.58191,-2.77071 31.53321,-20.91406 0,0 -9.79946,-12.99815 -25.69727,-12.54688 z";
    private const string SvgF2_Tibia = "m 199.44316,188.44878 c -2.55559,0 -12.37758,0.39889 -13.69141,2.36523 -0.20738,1.93792 3.2505,23.15004 15.44724,44.9668 0,0 4.19173,3.23884 1.71484,5.90625 -2.47689,2.66742 -5.33384,24.00701 12.95703,40.39258 4.68029,4.19275 8.7118,8.85133 11.85352,9.91406 2.92986,-0.71999 3.70201,-6.93512 1.625,-12.75 -0.2533,-0.67882 -7.0637,-6.65596 -6.12305,-17.23828 1.0778,-12.12525 -1.75195,-18.1875 -1.75195,-18.1875 0,0 -10.77877,-53.3514 -15.49414,-52.8125 -3.57751,0.40885 -6.07289,-2.55664 -6.53711,-2.55664 z";
    private const string SvgF2_Tarsus = "m 165.32823,119.60394 c -0.48168,-0.10084 -2.16963,1.74767 -1.22656,3.13282 1.0778,1.58302 -3.94032,-0.57264 -3.60351,-1.78516 0.33681,-1.21253 -2.52693,1.98821 0.4707,3.36914 2.99763,1.38093 0.23729,0.50436 0.16992,1.98633 10.1232,21.39316 20.83781,43.13451 25.1895,66.19922 1.13608,0.5778 4.50798,0.25597 5.65664,-0.0628 3.6922,-1.02452 4.95397,-1.18379 7.9293,-3.11498 0.83271,-0.49513 0.18225,-1.12945 0.0303,-2.04416 -0.59645,-3.59086 -25.50223,-64.7158 -29.54398,-65.38943 -4.04174,-0.67362 -4.88381,-1.31274 -4.95117,-2.12109 -0.008,-0.10105 -0.0523,-0.15552 -0.1211,-0.16993 z";

    private const string SvgM_Femur = "m 263.84051,312.21764 c -19.88086,0.16342 -51.93048,11.51566 -45.49414,18.63672 0.85818,0.94947 1.67689,3.25179 2.09765,4.57422 0.61149,1.92184 -1.05364,10.46065 -0.0586,11.11328 2.00576,1.09346 40.25063,-1.66082 47.26367,-6.53516 8.64715,-6.0101 15.09791,-10.29563 10.90625,-21.91796 -1.01814,-4.28693 -6.93538,-5.93505 -14.71484,-5.8711 z";
    private const string SvgM_Tibia = "m 134.64193,303.41992 c -1.70472,0.12318 -2.91511,0.57035 -3.46289,1.42774 -0.48896,0.76532 -1.95165,0.66434 -2.84721,1.53698 -0.43925,0.5547 -0.49686,0.8392 -0.58247,1.51184 -0.40916,3.21483 -4.87074,9.41633 -4.55469,10.51172 0.1986,0.98932 3.60351,2.0625 3.60351,2.0625 0,0 20.00531,7.8119 32.19922,8.95508 12.19391,1.14318 20.80182,2.85234 21.33985,5.7168 0.5359,2.85308 15.28563,6.10219 25.79101,9.45703 2.91257,0.93011 18.09009,1.57293 18.91797,1.29687 1.41018,-0.7613 -3.82395,-12.82761 -5.25,-13.11328 -2.27689,0.44111 -29.13113,-13.60585 -32.98242,-14.4082 -4.00113,-0.83357 -40.23883,-15.8173 -52.17188,-14.95508 z";
    private const string SvgM_Tarsus = "m 41.904735,285.27734 c -3.275663,-0.12257 -5.895245,0.70846 -7.324219,3.56641 -4.572717,9.14544 34.296875,8.57422 34.296875,8.57422 0,0 4.953201,-0.7636 6.667969,0.95117 1.714769,1.71476 28.00858,13.90935 35.82032,15.43359 4.86859,0.94997 8.99038,3.54848 12.35338,4.64698 1.31519,-0.0125 11.02008,-8.25983 8.40804,-11.83895 -0.19296,-0.2644 -0.66651,-0.9073 -1.71937,-1.00777 -7.46626,4.13259 -52.084082,-19.88301 -63.055729,-17.1401 -7.859358,1.96484 -18.240808,-2.91589 -25.447266,-3.18555 z";

    private const string SvgB_Femur = "m 295.73825,358.4498 c -17.24877,0.21875 -33.06304,12.68796 -36.38672,19.61719 -3.79851,7.91911 -5.60109,14.66066 -4.44532,20.03516 0.12983,0.60372 -0.12547,1.18411 0.10352,1.63867 1.64141,2.13707 9.13149,5.7161 11.55664,4.11133 6.1954,-4.8427 51.42548,-29.96755 36.62109,-44.69532 -2.49036,-0.52068 -4.9851,-0.73828 -7.44921,-0.70703 z";
    private const string SvgB_Tibia = "m 255.48755,398.29554 c -0.36222,3.91318 -5.51953,4.64258 -5.51953,4.64258 0,0 -26.58038,11.75304 -30.82227,21.66211 -4.24189,9.90908 -35.39013,44.66283 -37.57226,51.09961 -2.08063,6.13736 -3.45552,8.6036 -3.81446,11.67773 0.0955,3.20001 4.67127,8.20523 8.23304,7.85709 3.82948,-1.29627 39.12473,-43.08578 41.22204,-48.06412 2.40948,-5.71931 4.05926,-15.90317 8.54883,-14.74804 4.48957,1.15512 32.56636,-23.48813 30.625,-28.89454 -0.55238,-1.5383 -9.54893,-6.15867 -10.90039,-5.23242 z";
    private const string SvgB_Tarsus = "m 177.67635,486.81564 c -6.90844,13.30631 -65.56994,90.04207 -64.84475,92.4387 0.63037,2.08326 -2.66574,7.34114 -6.45313,5.79492 -3.78738,-1.5462 2.79556,4.43791 5.24024,1.06445 2.44469,-3.37346 -1.32813,4.56836 -1.32813,4.56836 0,0 3.81483,-0.17032 4.45117,-2.8164 0.63633,-2.64609 1.68964,-4.29841 4.10156,-3.44336 2.39471,0.84895 68.00326,-89.48089 68.94336,-90.77539 0.30736,-1.89986 -8.35226,-7.54165 -10.11032,-6.83128 z";

    private static readonly Geometry CarapaceAndPalpsGeo;
    private static readonly Geometry AbdomenGeo;

    private static readonly LegArt ArtF1;
    private static readonly LegArt ArtF2;
    private static readonly LegArt ArtM;
    private static readonly LegArt ArtB;

    static SpiderRenderer()
    {
        CarapaceAndPalpsGeo = PrepareCarapaceAndPalps();
        AbdomenGeo = PrepareAbdomen();

        // Front leg 1
        ArtF1 = new LegArt(
            SvgF1_Femur,
            new Point(289, 250), new Point(247, 244),
            SvgF1_Tibia,
            new Point(244, 127),
            SvgF1_Tarsus,
            new Point(243, 55));

        // Front leg 2
        ArtF2 = new LegArt(
            SvgF2_Femur,
            new Point(281, 286), new Point(232, 287),
            SvgF2_Tibia,
            new Point(198, 193),
            SvgF2_Tarsus,
            new Point(165, 121));

        // Middle leg
        ArtM = new LegArt(
            SvgM_Femur,
            new Point(275, 331), new Point(223, 337),
            SvgM_Tibia,
            new Point(129, 313),
            SvgM_Tarsus,
            new Point(42, 290));

        // Back leg
        ArtB = new LegArt(
            SvgB_Femur,
            new Point(304, 375), new Point(262, 398),
            SvgB_Tibia,
            new Point(181, 488),
            SvgB_Tarsus,
            new Point(109, 585));
    }

    private static readonly double SvgCarapaceHeight = StreamGeometry.Parse(SvgCarapace).Bounds.Height;
    
    public static void Draw(DrawingContext ctx, Spider spider)
    {
        // Calculate the common SVG-to-world scale.
        double targetCarapaceHeight = 5.0 * spider.PixelsPerUnit;
        double svgScale = targetCarapaceHeight / SvgCarapaceHeight;

        // 1. Draw SVG legs, rear to front.
        var legs = spider.Legs;

        for (int i = legs.Count - 1; i >= 0; i--)
        {
            SpiderLeg leg = legs[i];

            LegArt art = GetArtForLeg(leg);
            bool isRight = leg.Side > 0;

            // Draw distal segments first, proximal segments last.
            // This helps hide small connection seams.
            DrawSegment(
                ctx, art.Tarsus,
                leg.Ankle, leg.Tip,
                isRight, svgScale);

            DrawSegment(
                ctx, art.Tibia,
                leg.Knee, leg.Ankle,
                isRight, svgScale);

            DrawSegment(
                ctx, art.Femur,
                leg.Hip, leg.Knee,
                isRight, svgScale);
        }
        
        double jointRadius = Math.Max(0.45, 6.0 * svgScale);

        foreach (var leg in spider.Legs)
        {
            DrawJoint(ctx, leg.Knee, jointRadius);
            DrawJoint(ctx, leg.Ankle, jointRadius);
        }

        // 2. Draw abdomen.
        Vec2 pedicel =
            spider.Position +
            new Vec2(0.0, 2.6 * spider.PixelsPerUnit)
                .Rotated(spider.HeadingDeg);

        using (ctx.PushTransform(
                   Matrix.CreateScale(svgScale, svgScale) *
                   Matrix.CreateRotation(
                       spider.AbdomenDeg * MathUtil.DegToRad) *
                   Matrix.CreateTranslation(pedicel.X, pedicel.Y)))
        {
            ctx.DrawGeometry(Ink, null, AbdomenGeo);
        }

        // 3. Draw carapace and palps.
        using (ctx.PushTransform(
                   Matrix.CreateScale(svgScale, svgScale) *
                   Matrix.CreateRotation(
                       spider.HeadingDeg * MathUtil.DegToRad) *
                   Matrix.CreateTranslation(
                       spider.Position.X,
                       spider.Position.Y)))
        {
            ctx.DrawGeometry(Ink, null, CarapaceAndPalpsGeo);
        }
    }

    public static void DrawBodyRim(DrawingContext ctx, Spider spider, Pen pen)
    {
        double svgScale = 5.0 * spider.PixelsPerUnit / SvgCarapaceHeight;
        // the art is drawn through a scale transform, which would shrink the stroke too
        var scaled = new Pen(pen.Brush, pen.Thickness / svgScale) { LineJoin = PenLineJoin.Round, LineCap = PenLineCap.Round };

        Vec2 pedicel = spider.Position + new Vec2(0.0, 2.6 * spider.PixelsPerUnit).Rotated(spider.HeadingDeg);

        using (ctx.PushTransform(
                   Matrix.CreateScale(svgScale, svgScale) *
                   Matrix.CreateRotation(spider.AbdomenDeg * MathUtil.DegToRad) *
                   Matrix.CreateTranslation(pedicel.X, pedicel.Y)))
            ctx.DrawGeometry(null, scaled, AbdomenGeo);

        using (ctx.PushTransform(
                   Matrix.CreateScale(svgScale, svgScale) *
                   Matrix.CreateRotation(spider.HeadingDeg * MathUtil.DegToRad) *
                   Matrix.CreateTranslation(spider.Position.X, spider.Position.Y)))
            ctx.DrawGeometry(null, scaled, CarapaceAndPalpsGeo);
    }
    
    private const double SvgLegThicknessScale = 0.85;

    private static void DrawSegment(
        DrawingContext ctx,
        SegmentArt art,
        Vec2 start,
        Vec2 end,
        bool isRight,
        double svgScale)
    {
        double ikDx = end.X - start.X;
        double ikDy = end.Y - start.Y;

        double ikLen = Math.Sqrt(
            ikDx * ikDx +
            ikDy * ikDy);

        if (ikLen < 0.001 || art.NativeLen < 0.001)
            return;

        // Target IK direction.
        double ikAngle = Math.Atan2(ikDy, ikDx);

        // Mirror the original left-side SVG for the right side.
        double mirrorX = isRight ? -1.0 : 1.0;

        double nativeDx = art.NativeDx * mirrorX;
        double nativeDy = art.NativeDy;

        double nativeAngle = Math.Atan2(
            nativeDy,
            nativeDx);

        // Stretch only along the bone to fit the IK length.
        double lengthScale = ikLen / art.NativeLen;

        // Preserve the SVG's original thickness relative to the body.
        double widthScale = svgScale * SvgLegThicknessScale;

        Matrix mirror = isRight
            ? Matrix.CreateScale(-1.0, 1.0)
            : Matrix.Identity;

        using (ctx.PushTransform(
                   mirror *
                   Matrix.CreateRotation(-nativeAngle) *
                   Matrix.CreateScale(lengthScale, widthScale) *
                   Matrix.CreateRotation(ikAngle) *
                   Matrix.CreateTranslation(start.X, start.Y)))
        {
            ctx.DrawGeometry(Ink, null, art.Geometry);
        }
    }

    /*private static LegArt GetArtForLeg(Spider spider, Vec2 hip, out bool isRight)
    {
        double angleToHipDeg = GetAngleRad(spider.Position, hip) * 180.0 / Math.PI;
        double localAngle = angleToHipDeg - spider.HeadingDeg;
        localAngle = (localAngle % 360.0 + 360.0) % 360.0;
        
        isRight = localAngle < 180;
        double sideAngle = isRight ? localAngle : (360.0 - localAngle);
        
        if (sideAngle < 45) return ArtF1;
        if (sideAngle < 85) return ArtF2;
        if (sideAngle < 130) return ArtM;
        return ArtB;
    }*/
    
    private static LegArt GetArtForLeg(SpiderLeg leg)
    {
        return leg.Index switch
        {
            0 => ArtF1,
            1 => ArtF2,
            2 => ArtM,
            3 => ArtB,
            _ => throw new ArgumentOutOfRangeException(
                nameof(leg.Index))
        };
    }

    private static double GetAngleRad(Vec2 a, Vec2 b) => Math.Atan2(b.Y - a.Y, b.X - a.X);
    private static double GetDist(Vec2 a, Vec2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static Geometry PrepareCarapaceAndPalps()
    {
        var carapace = StreamGeometry.Parse(SvgCarapace);
        var group = new GeometryGroup();
        group.Children.Add(carapace);
        group.Children.Add(StreamGeometry.Parse(SvgPalpLeft));
        group.Children.Add(StreamGeometry.Parse(SvgPalpRight));
        
        group.Transform = new TranslateTransform(-carapace.Bounds.Center.X, -carapace.Bounds.Center.Y);
        return group;
    }

    private static Geometry PrepareAbdomen()
    {
        var abdomen = StreamGeometry.Parse(SvgAbdomen);
        var group = new GeometryGroup();
        group.Children.Add(abdomen);
        
        double pivotX = abdomen.Bounds.Center.X;
        double pivotY = abdomen.Bounds.Top + (abdomen.Bounds.Height * 0.02);
        group.Transform = new TranslateTransform(-pivotX, -pivotY);
        return group;
    }

    private sealed class SegmentArt
    {
        public Geometry Geometry { get; }

        public double NativeDx { get; }
        public double NativeDy { get; }
        public double NativeLen { get; }

        public SegmentArt(
            string pathData,
            Point proximal,
            Point distal)
        {
            var geo = StreamGeometry.Parse(pathData);

            // Move the proximal joint to (0, 0).
            var group = new GeometryGroup();
            group.Children.Add(geo);
            group.Transform = new TranslateTransform(
                -proximal.X,
                -proximal.Y);

            Geometry = group;

            // The actual native bone direction, not the bounds diagonal.
            NativeDx = distal.X - proximal.X;
            NativeDy = distal.Y - proximal.Y;

            NativeLen = Math.Sqrt(
                NativeDx * NativeDx +
                NativeDy * NativeDy);
        }
    }

    private sealed class LegArt
    {
        public SegmentArt Femur { get; }
        public SegmentArt Tibia { get; }
        public SegmentArt Tarsus { get; }

        public LegArt(
            string femurSvg,
            Point hip,
            Point knee,
            string tibiaSvg,
            Point ankle,
            string tarsusSvg,
            Point tip)
        {
            Femur = new SegmentArt(femurSvg, hip, knee);
            Tibia = new SegmentArt(tibiaSvg, knee, ankle);
            Tarsus = new SegmentArt(tarsusSvg, ankle, tip);
        }
    }
    
    private static void DrawLegOutline(
        DrawingContext ctx,
        IReadOnlyList<Vec2> outline)
    {
        if (outline == null || outline.Count < 3)
            return;

        var geometry = new StreamGeometry();

        using (var builder = geometry.Open())
        {
            builder.BeginFigure(
                new Point(outline[0].X, outline[0].Y),
                true);

            for (int i = 1; i < outline.Count; i++)
            {
                builder.LineTo(
                    new Point(outline[i].X, outline[i].Y));
            }

            builder.EndFigure(true);
        }

        ctx.DrawGeometry(Ink, null, geometry);
    }
    
    private static void DrawJoint(
        DrawingContext ctx,
        Vec2 position,
        double radius)
    {
        ctx.DrawEllipse(
            Ink,
            null,
            new Point(position.X, position.Y),
            radius,
            radius);
    }
}