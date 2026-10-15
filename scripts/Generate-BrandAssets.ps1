[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (!$IsWindows) { throw 'Brand asset export uses the Windows System.Drawing renderer.' }
$null = . (Join-Path $PSScriptRoot 'Initialize-Workspace.ps1')
$brandRoot = Split-Path -Parent $PSScriptRoot
$brandAssets = Join-Path $brandRoot 'src/SmartSQL.Desktop/Assets'
[xml]$brandSvg = Get-Content -LiteralPath (Join-Path $brandAssets 'SmartSQL-mark.svg') -Raw
$brandNamespace = [Xml.XmlNamespaceManager]::new($brandSvg.NameTable)
$brandNamespace.AddNamespace('s', 'http://www.w3.org/2000/svg')
$brandPath = $brandSvg.SelectSingleNode('//s:path[@id="ribbon"]', $brandNamespace).GetAttribute('d')
$brandPath += ' ' + $brandSvg.SelectSingleNode('//s:path[@id="lower-ribbon"]', $brandNamespace).GetAttribute('d')

Add-Type -AssemblyName System.Drawing.Common
$brandDrawingReferences = @([System.Drawing.Bitmap].Assembly.Location, [System.Drawing.PointF].Assembly.Location,
    [System.Text.RegularExpressions.Regex].Assembly.Location) + @(Get-ChildItem -LiteralPath $PSHOME -Filter 'System.Private.Windows.*.dll' | ForEach-Object FullName)
if (!('SmartSqlBrandExporter' -as [type])) {
    Add-Type -ReferencedAssemblies $brandDrawingReferences -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

public static class SmartSqlBrandExporter
{
    // The canonical SVG uses only absolute M/L/Q/Z commands.
    static GraphicsPath Parse(string svg)
    {
        var tokens = Regex.Matches(svg, @"[MLQZ]|-?\d+(?:\.\d+)?");
        var path = new GraphicsPath(); float x=0,y=0; int i=0;
        Func<float> number = () => float.Parse(tokens[i++].Value,CultureInfo.InvariantCulture);
        while(i<tokens.Count)
        {
            var command=tokens[i++].Value;
            if(command=="Z") { path.CloseFigure(); continue; }
            if(command=="M") { x=number(); y=number(); path.StartFigure(); continue; }
            if(command=="L") { float nx=number(),ny=number(); path.AddLine(x,y,nx,ny); x=nx; y=ny; continue; }
            if(command=="Q")
            {
                float cx=number(),cy=number(),nx=number(),ny=number();
                path.AddBezier(x,y,x+(cx-x)*2/3,y+(cy-y)*2/3,nx+(cx-nx)*2/3,ny+(cy-ny)*2/3,nx,ny);
                x=nx; y=ny; continue;
            }
            throw new InvalidDataException("Unsupported canonical SVG command.");
        }
        return path;
    }
    public static byte[] Render(string svg,int size)
    {
        const int supersampling=4;
        using var bitmap=new Bitmap(size*supersampling,size*supersampling,PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent); graphics.SmoothingMode=SmoothingMode.AntiAlias;
            graphics.ScaleTransform(size*supersampling/128f,size*supersampling/128f);
            using var tile=new GraphicsPath(); const float x=3,y=3,w=122,h=122,r=25;
            tile.AddArc(x,y,2*r,2*r,180,90); tile.AddArc(x+w-2*r,y,2*r,2*r,270,90);
            tile.AddArc(x+w-2*r,y+h-2*r,2*r,2*r,0,90); tile.AddArc(x,y+h-2*r,2*r,2*r,90,90); tile.CloseFigure();
            using var background=new SolidBrush(Color.FromArgb(248,252,249));
            using var border=new Pen(Color.FromArgb(199,216,206),1);
            graphics.FillPath(background,tile); graphics.DrawPath(border,tile);
            using var top=Parse(svg);
            using var placement=new Matrix(0.9f,0,0,0.9f,19,13.6f); top.Transform(placement);
            // Flat green improves 16–32px edges; larger assets retain the reference gradient.
            using Brush green=size<=32 ? (Brush)new SolidBrush(Color.FromArgb(30,116,84))
                : new LinearGradientBrush(new PointF(20,14),new PointF(105,115),Color.FromArgb(57,152,120),Color.FromArgb(20,86,64));
            graphics.FillPath(green,top);
        }
        using var reduced=new Bitmap(size,size,PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(reduced))
        {
            graphics.InterpolationMode=InterpolationMode.HighQualityBicubic; graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
            graphics.DrawImage(bitmap,new Rectangle(0,0,size,size));
        }
        using var stream=new MemoryStream(); reduced.Save(stream,ImageFormat.Png); return stream.ToArray();
    }
    public static void Export(string svg,string directory)
    {
        int[] sizes={16,20,24,32,40,48,64,128,256}; var frames=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++) frames[i]=Render(svg,sizes[i]);
        using(var output=File.Create(Path.Combine(directory,"SmartSQL.ico")))
        using(var writer=new BinaryWriter(output))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length); int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++)
            {
                writer.Write((byte)(sizes[i]==256?0:sizes[i])); writer.Write((byte)(sizes[i]==256?0:sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(frames[i].Length); writer.Write(offset); offset+=frames[i].Length;
            }
            foreach(var frame in frames) writer.Write(frame);
        }
        File.WriteAllBytes(Path.Combine(directory,"SmartSQL.png"),Render(svg,512));
    }
}
'@
}
[SmartSqlBrandExporter]::Export($brandPath,$brandAssets)
Write-Output 'Exported SmartSQL.ico (16–256px) and SmartSQL.png (512px) from SmartSQL-mark.svg.'
