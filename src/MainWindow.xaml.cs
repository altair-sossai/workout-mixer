using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WorkoutMixer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        double largura = 800;
        double altura = 400;

        double margemEsq = 60;
        double margemDir = 40;
        double margemTop = 20;
        double margemBottom = 50;

        Random rand = new Random();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            DesenharGrafico();
        }

        void DesenharGrafico()
        {
            double larguraUtil = largura - margemEsq - margemDir;
            double alturaUtil = altura - margemTop - margemBottom;

            // =======================
            // DADOS
            // =======================
            var dados = new List<(double tempo, double intensidade)>();

            for (int t = 0; t <= 60; t += 2)
            {
                double intensidade =
                    0.5 + 0.4 * Math.Sin(t / 8.0) + (rand.NextDouble() - 0.5) * 0.2;

                intensidade = Math.Max(0, Math.Min(1, intensidade));

                dados.Add((t, intensidade));
            }

            // =======================
            // CONVERTER PARA PONTOS
            // =======================
            var pontos = new List<Point>();

            foreach (var d in dados)
            {
                double x = margemEsq + (d.tempo / 60.0) * larguraUtil;
                double y = margemTop + alturaUtil - d.intensidade * alturaUtil;

                pontos.Add(new Point(x, y));
            }

            // =======================
            // ZONAS
            // =======================
            var zonas = new List<(string nome, Brush cor, double max)>
            {
                ("Cinza", Brushes.Gray, 0.30),
                ("Azul", Brushes.Blue, 0.40),
                ("Verde", Brushes.Green, 0.50),
                ("Amarelo", Brushes.Gold, 0.80),
                ("Vermelho", Brushes.Red, 1.00)
            };

            var duracoes = GerarIntervalos(60, 5);

            double tempoAtual = 0;

            for (int i = 0; i < zonas.Count; i++)
            {
                var zona = zonas[i];
                double duracao = duracoes[i];

                double x = margemEsq + (tempoAtual / 60.0) * larguraUtil;
                double width = (duracao / 60.0) * larguraUtil;

                double y = margemTop + alturaUtil - zona.max * alturaUtil;
                double height = zona.max * alturaUtil;

                var rect = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Fill = zona.cor,
                    Opacity = 0.7,
                    ToolTip = $"{zona.nome}\nDuração: {duracao} min\nAté {zona.max * 100:0}%"
                };

                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);

                GraficoCanvas.Children.Add(rect);

                tempoAtual += duracao;
            }

            // =======================
            // LINHA SUAVE
            // =======================
            var path = new Path
            {
                Stroke = Brushes.Black,
                Opacity = 0.5,
                StrokeThickness = 2
            };

            var geometry = new PathGeometry();
            var figure = new PathFigure
            {
                StartPoint = pontos[0]
            };

            double suavidade = 0.2;

            for (int i = 0; i < pontos.Count - 1; i++)
            {
                var p0 = i > 0 ? pontos[i - 1] : pontos[i];
                var p1 = pontos[i];
                var p2 = pontos[i + 1];
                var p3 = i < pontos.Count - 2 ? pontos[i + 2] : p2;

                var cp1 = new Point(
                    p1.X + (p2.X - p0.X) * suavidade,
                    p1.Y + (p2.Y - p0.Y) * suavidade
                );

                var cp2 = new Point(
                    p2.X - (p3.X - p1.X) * suavidade,
                    p2.Y - (p3.Y - p1.Y) * suavidade
                );

                figure.Segments.Add(new BezierSegment(cp1, cp2, p2, true));
            }

            geometry.Figures.Add(figure);
            path.Data = geometry;

            GraficoCanvas.Children.Add(path);

            // =======================
            // PONTOS
            // =======================
            for (int i = 0; i < pontos.Count; i++)
            {
                var p = pontos[i];
                var d = dados[i];

                var circle = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = Brushes.Black,
                    ToolTip = $"Tempo: {d.tempo} min\nIntensidade: {d.intensidade:0.00}"
                };

                Canvas.SetLeft(circle, p.X - 3);
                Canvas.SetTop(circle, p.Y - 3);

                GraficoCanvas.Children.Add(circle);
            }

            // =======================
            // EIXOS
            // =======================
            AddLine(margemEsq, altura - margemBottom, largura - margemDir, altura - margemBottom);
            AddLine(margemEsq, margemTop, margemEsq, altura - margemBottom);

            // =======================
            // TICKS X
            // =======================
            foreach (var t in new[] { 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60 })
            {
                double x = margemEsq + (t / 60.0) * larguraUtil;

                AddText($"{t}m", x, altura - margemBottom + 5, HorizontalAlignment.Center);
            }

            // =======================
            // TICKS Y
            // =======================
            foreach (var v in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
            {
                double y = margemTop + alturaUtil - v * alturaUtil;

                AddText($"{v:0.##}", margemEsq - 10, y - 8, HorizontalAlignment.Right);
            }
        }

        // =======================
        // HELPERS
        // =======================
        void AddLine(double x1, double y1, double x2, double y2)
        {
            GraficoCanvas.Children.Add(new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = Brushes.Black
            });
        }

        void AddText(string text, double x, double y, HorizontalAlignment align)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 11
            };

            GraficoCanvas.Children.Add(tb);

            if (align == HorizontalAlignment.Center)
                Canvas.SetLeft(tb, x - 10);
            else
                Canvas.SetLeft(tb, x - 30);

            Canvas.SetTop(tb, y);
        }

        List<double> GerarIntervalos(double total, int partes)
        {
            var valores = new List<double>();
            double restante = total;

            for (int i = 0; i < partes - 1; i++)
            {
                double v = rand.Next(5, (int)(restante / 2));
                valores.Add(v);
                restante -= v;
            }

            valores.Add(restante);
            return valores;
        }
    }

}