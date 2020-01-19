using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;

namespace wabisabi_extract
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                System.Console.WriteLine("参数太少");
                return;
            }

            DicingToImage image = new DicingToImage();
            image.Handle(args[0], args[1]);

            System.Console.WriteLine("处理完成...");

            // 暂停等待着
            System.Console.ReadLine();
        }
    }
}

public class DicingToImage
{
    struct Image_info
    {
        public string name;
        public string atlas_name;
        public string dir_name;
        public int w;
        public int h;
        public List<int> cell_indexs;
    }

    private ConcurrentQueue<string> file_queue;

    public string source_path;
    public string target_path;

    public void Handle(string src_path, string dst_path)
    {
        source_path = Path.Combine(src_path);
        target_path = Path.Combine(dst_path);

        System.Console.WriteLine("读取描述文件列表");
        string[] desc_files = Directory.GetFiles(src_path, "*.txt");

        file_queue = new ConcurrentQueue<string>(desc_files);

        // 开启多线程
        int thread_count = 4;
        Task[] tasks = new Task[thread_count];

        for (int i = 0; i < thread_count; i++)
        {
            tasks[i] = Task.Factory.StartNew(Handle_file);
        }

        Task.WaitAll(tasks);
    }

    /**
     * 处理文件，多线程安全
     */
    private void Handle_file()
    {
        while (file_queue.TryDequeue(out string file))
        {
            System.Console.WriteLine("处理文件：{0}", file);
            string text = File.ReadAllText(file);
            List<Image_info> infos = Build_info_object(text);

            Dictionary<string, Bitmap> atlas_dict = new Dictionary<string, Bitmap>();

            foreach (Image_info info in infos)
            {
                if (!Directory.Exists(Path.Combine(target_path, info.dir_name)))
                    Directory.CreateDirectory(Path.Combine(target_path, info.dir_name));

                Convert_Image(info, atlas_dict);
            }
        }
    }

    /**
     * 转换图像
     */
    private void Convert_Image(Image_info info, Dictionary<string, Bitmap> atlas_Bitmaps)
    {
        var path = Path.Combine(source_path, info.atlas_name + ".png");
        if (!atlas_Bitmaps.ContainsKey(path))
        {
            System.Console.WriteLine("载入地图集：{0}", info.atlas_name);
            // 垂直翻转画布，因为是UV坐标
            Bitmap atlas_bitmap = Image.FromFile(path) as Bitmap;
            atlas_bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);
            atlas_Bitmaps.Add(path, atlas_bitmap);
        }

        Bitmap atlas_image = atlas_Bitmaps[path];

        int[] index = info.cell_indexs.ToArray();

        Bitmap dst_bitmap = new Bitmap(info.w, info.h, PixelFormat.Format24bppRgb);

        Graphics g;

        //要绘制到的位图
        g = Graphics.FromImage(dst_bitmap);

        int cellWidth = info.w;
        int cellHeight = info.h;

        int cellSize = 64;
        int padding = 3;
        int paddingCellSize = cellSize - padding * 2;

        int cellCountX = (int)Math.Ceiling(1.0f * cellWidth / paddingCellSize);
        int cellCountY = (int)Math.Ceiling(1.0f * cellHeight / paddingCellSize);

        int atlasWidth = atlas_image.Width;
        int atlasHeight = atlas_image.Height;
        int atlasCellCountX = (int)Math.Ceiling(1.0f * atlasWidth / cellSize);

        int i = 0;
        for (int cellY = 0; cellY < cellCountY; ++cellY)
        {
            int y0 = cellY * paddingCellSize;
            for (int cellX = 0; cellX < cellCountX; ++cellX)
            {
                int x0 = cellX * paddingCellSize;
                int cellIndex = index[i];

                int ux = (cellIndex % atlasCellCountX) * cellSize;
                int uy = (cellIndex / atlasCellCountX) * cellSize;

                //复制图像
                g.DrawImage(atlas_image, new Rectangle(x0, y0, cellSize, cellSize), new Rectangle(ux, uy, cellSize, cellSize), GraphicsUnit.Pixel);
                i++;
            }
        }

        dst_bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);

        string save_file_path = Path.Combine(target_path, info.dir_name, info.name + ".png");

        dst_bitmap.Save(save_file_path, ImageFormat.Png);

        System.Console.WriteLine("写入文件：{0}", save_file_path);
    }

    /**
     * 构建信息对象，返回包含每个数据信息的结构
     */
    private List<Image_info> Build_info_object(string descrition)
    {
        List<Image_info> ret = new List<Image_info>();

        // 切割数据单元格
        string pattern = @"(\s*?DicingTextureData data\s*(?:.|\r|\n)*?int transparentIndex = -?\d+?)";

        // 目录名
        Match dir_name_match = Regex.Match(descrition, "string m_Name = \"(\\w*)\"");
        string dir_name = dir_name_match.Groups[1].ToString();

        foreach (Match match in Regex.Matches(descrition, pattern))
        {
            // 构建数据
            Image_info info = new Image_info();

            Match name_match = Regex.Match(match.Value, "string name = \"(\\w*)\"");
            info.name = name_match.Groups[1].ToString();

            Match atlasName_match = Regex.Match(match.Value, "string atlasName = \"(\\w*)\"");
            info.atlas_name = atlasName_match.Groups[1].ToString();

            Match width_match = Regex.Match(match.Value, "int width = (\\d+)");
            info.w = Convert.ToInt32(width_match.Groups[1].ToString());

            Match height_match = Regex.Match(match.Value, "int height = (\\d+)");
            info.h = Convert.ToInt32(height_match.Groups[1].ToString());

            info.dir_name = dir_name;

            info.cell_indexs = new List<int>();

            foreach (Match cell_index in Regex.Matches(match.Groups[1].ToString(), "data = (\\d+)"))
            {
                info.cell_indexs.Add(Convert.ToInt32(cell_index.Groups[1].ToString()));
            }

            ret.Add(info);
        }

        return ret;
    }
}
