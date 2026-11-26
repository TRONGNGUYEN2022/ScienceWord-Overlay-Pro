using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScienceWordOverlay.Utils
{
    public static class LatexMathRenderer
    {
        private sealed class BoxMetrics
        {
            public double W;
            public double Ascent;
            public double Descent;
        }

        private sealed class BoxNode
        {
            public string Kind = "text"; // space | text | group | frac | sqrt | bigop | lim | accent | scripted | matrix
            public string Text = "";
            public bool Italic = false;
            public double SpaceW = 0;

            public List<BoxNode>? Children;
            public List<BoxNode>? Num;
            public List<BoxNode>? Den;
            public List<BoxNode>? Deg;
            public List<BoxNode>? Arg;
            public List<BoxNode>? BaseNodes;
            public List<BoxNode>? Sub;
            public List<BoxNode>? Sup;
            public List<BoxNode>? Body;

            public string Sym = "";
            public bool IsCenteredLimit = false;
            public string Accent = "vec"; // vec | hat | bar
            public string Env = "cases";
            public List<List<List<BoxNode>>>? Rows;

            // Cached measurement results
            public BoxMetrics M = new();
            public BoxMetrics? MNum, MDen, MArg, MDeg, MSub, MSup, MBody, MBase;
            public double SubFs, DegFs, OpFs, SymW, LimFs, LimW, ScrFs, CellFs, BraceW, RightW, TotalH;
            public double[]? ColWidths;
            public List<(List<BoxMetrics> cellsM, double rAsc, double rDesc)>? RowMetrics;
        }

        private static readonly Dictionary<string, string> SymbolMap = new()
        {
            ["\\alpha"] = "α", ["\\beta"] = "β", ["\\gamma"] = "γ", ["\\delta"] = "δ", ["\\Delta"] = "Δ",
            ["\\epsilon"] = "ε", ["\\varepsilon"] = "ε", ["\\zeta"] = "ζ", ["\\eta"] = "η",
            ["\\theta"] = "θ", ["\\vartheta"] = "ϑ", ["\\Theta"] = "Θ", ["\\iota"] = "ι", ["\\kappa"] = "κ",
            ["\\lambda"] = "λ", ["\\Lambda"] = "Λ", ["\\mu"] = "μ", ["\\nu"] = "ν", ["\\xi"] = "ξ",
            ["\\pi"] = "π", ["\\Pi"] = "Π", ["\\rho"] = "ρ", ["\\sigma"] = "σ", ["\\Sigma"] = "Σ",
            ["\\tau"] = "τ", ["\\upsilon"] = "υ", ["\\phi"] = "φ", ["\\varphi"] = "φ", ["\\Phi"] = "Φ",
            ["\\chi"] = "χ", ["\\psi"] = "ψ", ["\\Psi"] = "Ψ", ["\\omega"] = "ω", ["\\Omega"] = "Ω",
            ["\\infty"] = "∞", ["\\pm"] = "±", ["\\mp"] = "∓", ["\\times"] = "×", ["\\div"] = "÷",
            ["\\cdot"] = "·", ["\\ast"] = "∗", ["\\star"] = "⋆", ["\\circ"] = "°",
            ["\\neq"] = "≠", ["\\ne"] = "≠", ["\\le"] = "≤", ["\\leq"] = "≤", ["\\ge"] = "≥", ["\\geq"] = "≥",
            ["\\approx"] = "≈", ["\\equiv"] = "≡", ["\\sim"] = "∼", ["\\cong"] = "≅", ["\\propto"] = "∝",
            ["\\in"] = "∈", ["\\notin"] = "∉", ["\\ni"] = "∋", ["\\subset"] = "⊂", ["\\subseteq"] = "⊆",
            ["\\supset"] = "⊃", ["\\supseteq"] = "⊇", ["\\cup"] = "∪", ["\\cap"] = "∩",
            ["\\emptyset"] = "∅", ["\\varnothing"] = "∅",
            ["\\forall"] = "∀", ["\\exists"] = "∃", ["\\nexists"] = "∄", ["\\neg"] = "¬",
            ["\\land"] = "∧", ["\\lor"] = "∨", ["\\Rightarrow"] = "⇒", ["\\implies"] = "⇒",
            ["\\Leftrightarrow"] = "⇔", ["\\iff"] = "⇔", ["\\Leftarrow"] = "⇐",
            ["\\to"] = "→", ["\\rightarrow"] = "→", ["\\leftarrow"] = "←", ["\\leftrightarrow"] = "↔",
            ["\\mapsto"] = "↦", ["\\uparrow"] = "↑", ["\\downarrow"] = "↓",
            ["\\perp"] = "⊥", ["\\parallel"] = "∥", ["\\angle"] = "∠", ["\\triangle"] = "△",
            ["\\square"] = "□", ["\\partial"] = "∂", ["\\nabla"] = "∇", ["\\dots"] = "…", ["\\cdots"] = "⋯", ["\\ldots"] = "…"
        };

        private static readonly HashSet<string> UprightFuncs = new()
        {
            "\\sin", "\\cos", "\\tan", "\\cot", "\\sec", "\\csc",
            "\\arcsin", "\\arccos", "\\arctan", "\\sinh", "\\cosh", "\\tanh",
            "\\ln", "\\log", "\\lg", "\\exp", "\\det", "\\gcd", "\\max", "\\min", "\\sup", "\\inf", "\\arg", "\\deg"
        };

        private static readonly FontFamily MathFontFamily = new("Times New Roman");
        private static readonly FontFamily TextFontFamily = new("Segoe UI");

        private static Typeface GetTypeface(bool italic, bool plainUi = false) =>
            new(plainUi ? TextFontFamily : MathFontFamily,
                italic ? FontStyles.Italic : FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal);

        private static FormattedText MakeFt(string text, double fontSize, bool italic, Brush brush, double dpi, bool plainUi = false) =>
            new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, GetTypeface(italic, plainUi), fontSize, brush, dpi);

        private static bool LooksLikeLatex(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            return s.Contains('\\') || s.Contains('^') || s.Contains('_') || s.Contains('{');
        }

        private static (string content, int endIdx)? ExtractBraceGroup(string str, int startIdx, char openCh = '{', char closeCh = '}')
        {
            if (startIdx >= str.Length || str[startIdx] != openCh) return null;
            int depth = 0;
            for (int i = startIdx; i < str.Length; i++)
            {
                if (str[i] == '\\') { i++; continue; }
                if (str[i] == openCh) depth++;
                else if (str[i] == closeCh)
                {
                    depth--;
                    if (depth == 0)
                        return (str.Substring(startIdx + 1, i - startIdx - 1), i);
                }
            }
            return (str.Substring(startIdx + 1), str.Length - 1);
        }

        private static (string content, int nextIdx) ReadAtom(string str, int idx)
        {
            while (idx < str.Length && str[idx] == ' ') idx++;
            if (idx >= str.Length) return ("", idx);
            if (str[idx] == '{')
            {
                var grp = ExtractBraceGroup(str, idx, '{', '}');
                if (grp.HasValue) return (grp.Value.content, grp.Value.endIdx + 1);
            }
            if (str[idx] == '\\')
            {
                int j = idx + 1;
                while (j < str.Length && char.IsLetter(str[j])) j++;
                if (j == idx + 1 && j < str.Length) j++;
                return (str.Substring(idx, j - idx), j);
            }
            return (str[idx].ToString(), idx + 1);
        }

        private static List<BoxNode> ParseMixedLine(string line, bool forceMath)
        {
            if (string.IsNullOrEmpty(line)) return new List<BoxNode>();
            if (!forceMath && line.Contains('$'))
            {
                var result = new List<BoxNode>();
                int pos = 0;
                while (pos < line.Length)
                {
                    int open = line.IndexOf('$', pos);
                    if (open < 0)
                    {
                        string rem = line.Substring(pos);
                        if (rem.Length > 0) result.Add(new BoxNode { Kind = "text", Text = rem, Italic = false, Env = "plain" });
                        break;
                    }
                    if (open > pos)
                        result.Add(new BoxNode { Kind = "text", Text = line.Substring(pos, open - pos), Italic = false, Env = "plain" });

                    int close = line.IndexOf('$', open + 1);
                    if (close < 0)
                    {
                        result.AddRange(ParseLatexToBoxNodes(line.Substring(open + 1)));
                        break;
                    }
                    string mathPart = line.Substring(open + 1, close - open - 1);
                    result.AddRange(ParseLatexToBoxNodes(mathPart));
                    pos = close + 1;
                }
                return result;
            }

            if (!forceMath && !LooksLikeLatex(line))
                return new List<BoxNode> { new BoxNode { Kind = "text", Text = line, Italic = false, Env = "plain" } };

            return ParseLatexToBoxNodes(line);
        }

        private static List<BoxNode> ParseLatexToBoxNodes(string tex)
        {
            string s = (tex ?? "").Trim();
            var nodes = new List<BoxNode>();
            int i = 0, n = s.Length;

            while (i < n)
            {
                char ch = s[i];

                if (s.AsSpan(i).StartsWith("\\,") || s.AsSpan(i).StartsWith("\\;") || s.AsSpan(i).StartsWith("\\:") || s.AsSpan(i).StartsWith("\\!"))
                {
                    nodes.Add(new BoxNode { Kind = "space", SpaceW = s[i + 1] == '!' ? 0 : 4 });
                    i += 2;
                    continue;
                }
                if (s.AsSpan(i).StartsWith("\\quad")) { nodes.Add(new BoxNode { Kind = "space", SpaceW = 14 }); i += 5; continue; }
                if (s.AsSpan(i).StartsWith("\\qquad")) { nodes.Add(new BoxNode { Kind = "space", SpaceW = 24 }); i += 6; continue; }

                if (s.AsSpan(i).StartsWith("\\left"))
                {
                    i += 5;
                    if (i < n && (s[i] is '(' or '[' or '|' or '.'))
                    {
                        if (s[i] != '.') nodes.Add(new BoxNode { Kind = "text", Text = s[i].ToString(), Italic = false });
                        i++;
                    }
                    else if (i + 1 < n && s[i] == '\\' && s[i + 1] == '{')
                    {
                        nodes.Add(new BoxNode { Kind = "text", Text = "{", Italic = false });
                        i += 2;
                    }
                    continue;
                }
                if (s.AsSpan(i).StartsWith("\\right"))
                {
                    i += 6;
                    if (i < n && (s[i] is ')' or ']' or '|' or '.'))
                    {
                        if (s[i] != '.') nodes.Add(new BoxNode { Kind = "text", Text = s[i].ToString(), Italic = false });
                        i++;
                    }
                    else if (i + 1 < n && s[i] == '\\' && s[i + 1] == '}')
                    {
                        nodes.Add(new BoxNode { Kind = "text", Text = "}", Italic = false });
                        i += 2;
                    }
                    continue;
                }

                if (s.AsSpan(i).StartsWith("\\begin{"))
                {
                    var envGrp = ExtractBraceGroup(s, i + 6, '{', '}');
                    if (envGrp.HasValue)
                    {
                        string envName = envGrp.Value.content.Trim();
                        string endTag = $"\\end{{{envName}}}";
                        int endPos = s.IndexOf(endTag, envGrp.Value.endIdx + 1, StringComparison.Ordinal);
                        if (endPos >= 0)
                        {
                            string body = s.Substring(envGrp.Value.endIdx + 1, endPos - envGrp.Value.endIdx - 1);
                            // Strip optional column spec like {l} right after \begin{array}
                            if (envName == "array" && body.TrimStart().StartsWith("{"))
                            {
                                int bStart = body.IndexOf('{');
                                var colGrp = ExtractBraceGroup(body, bStart, '{', '}');
                                if (colGrp.HasValue) body = body.Substring(colGrp.Value.endIdx + 1);
                            }
                            var rawRows = body.Split(new[] { "\\\\" }, StringSplitOptions.RemoveEmptyEntries)
                                              .Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
                            var parsedRows = rawRows.Select(r => r.Split('&').Select(cell => ParseLatexToBoxNodes(cell.Trim())).ToList()).ToList();
                            nodes.Add(new BoxNode { Kind = "matrix", Env = envName, Rows = parsedRows });
                            i = endPos + endTag.Length;
                            continue;
                        }
                    }
                }

                if (s.AsSpan(i).StartsWith("\\frac") || s.AsSpan(i).StartsWith("\\dfrac") || s.AsSpan(i).StartsWith("\\tfrac"))
                {
                    int cmdLen = s.AsSpan(i).StartsWith("\\frac") ? 5 : 6;
                    var numA = ReadAtom(s, i + cmdLen);
                    var denA = ReadAtom(s, numA.nextIdx);
                    nodes.Add(new BoxNode
                    {
                        Kind = "frac",
                        Num = ParseLatexToBoxNodes(numA.content),
                        Den = ParseLatexToBoxNodes(denA.content)
                    });
                    i = denA.nextIdx;
                    continue;
                }

                if (s.AsSpan(i).StartsWith("\\sqrt"))
                {
                    int j = i + 5;
                    while (j < n && s[j] == ' ') j++;
                    List<BoxNode>? degNodes = null;
                    if (j < n && s[j] == '[')
                    {
                        var degGrp = ExtractBraceGroup(s, j, '[', ']');
                        if (degGrp.HasValue)
                        {
                            degNodes = ParseLatexToBoxNodes(degGrp.Value.content);
                            j = degGrp.Value.endIdx + 1;
                        }
                    }
                    var argA = ReadAtom(s, j);
                    nodes.Add(new BoxNode { Kind = "sqrt", Deg = degNodes, Arg = ParseLatexToBoxNodes(argA.content) });
                    i = argA.nextIdx;
                    continue;
                }

                if (s.AsSpan(i).StartsWith("\\int") || s.AsSpan(i).StartsWith("\\sum") || s.AsSpan(i).StartsWith("\\prod"))
                {
                    bool isSum = s.AsSpan(i).StartsWith("\\sum");
                    bool isProd = s.AsSpan(i).StartsWith("\\prod");
                    string sym = isSum ? "∑" : (isProd ? "∏" : "∫");
                    int j = i + (isProd ? 5 : 4);
                    List<BoxNode>? subNodes = null, supNodes = null;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        while (j < n && s[j] == ' ') j++;
                        if (j < n && s[j] == '_') { var a = ReadAtom(s, j + 1); subNodes = ParseLatexToBoxNodes(a.content); j = a.nextIdx; }
                        else if (j < n && s[j] == '^') { var a = ReadAtom(s, j + 1); supNodes = ParseLatexToBoxNodes(a.content); j = a.nextIdx; }
                    }
                    nodes.Add(new BoxNode { Kind = "bigop", Sym = sym, IsCenteredLimit = isSum || isProd, Sub = subNodes, Sup = supNodes });
                    i = j;
                    continue;
                }

                if (s.AsSpan(i).StartsWith("\\lim"))
                {
                    int j = i + 4;
                    while (j < n && s[j] == ' ') j++;
                    List<BoxNode>? subNodes = null;
                    if (j < n && s[j] == '_') { var a = ReadAtom(s, j + 1); subNodes = ParseLatexToBoxNodes(a.content); j = a.nextIdx; }
                    nodes.Add(new BoxNode { Kind = "lim", Sub = subNodes });
                    i = j;
                    continue;
                }

                if (s.AsSpan(i).StartsWith("\\overrightarrow") || s.AsSpan(i).StartsWith("\\vec") || s.AsSpan(i).StartsWith("\\widehat") || s.AsSpan(i).StartsWith("\\overline"))
                {
                    string accentType = "vec"; int cmdLen = 4;
                    if (s.AsSpan(i).StartsWith("\\overrightarrow")) { accentType = "vec"; cmdLen = 15; }
                    else if (s.AsSpan(i).StartsWith("\\widehat")) { accentType = "hat"; cmdLen = 8; }
                    else if (s.AsSpan(i).StartsWith("\\overline")) { accentType = "bar"; cmdLen = 9; }
                    var bodyA = ReadAtom(s, i + cmdLen);
                    nodes.Add(new BoxNode { Kind = "accent", Accent = accentType, Body = ParseLatexToBoxNodes(bodyA.content) });
                    i = bodyA.nextIdx;
                    continue;
                }

                if (s.AsSpan(i).StartsWith("\\text") || s.AsSpan(i).StartsWith("\\mathrm"))
                {
                    int cmdLen = s.AsSpan(i).StartsWith("\\text") ? 5 : 7;
                    var bodyA = ReadAtom(s, i + cmdLen);
                    nodes.Add(new BoxNode { Kind = "text", Text = bodyA.content, Italic = false, Env = "plain" });
                    i = bodyA.nextIdx;
                    continue;
                }

                if (ch == '\\')
                {
                    int j = i + 1;
                    while (j < n && char.IsLetter(s[j])) j++;
                    if (j == i + 1 && j < n)
                    {
                        nodes.Add(new BoxNode { Kind = "text", Text = s[j].ToString(), Italic = false });
                        i = j + 1;
                        continue;
                    }
                    string cmd = s.Substring(i, j - i);
                    if (UprightFuncs.Contains(cmd)) nodes.Add(new BoxNode { Kind = "text", Text = cmd.Substring(1), Italic = false });
                    else if (SymbolMap.TryGetValue(cmd, out string? mapped)) nodes.Add(new BoxNode { Kind = "text", Text = mapped, Italic = false });
                    else nodes.Add(new BoxNode { Kind = "text", Text = cmd.Substring(1), Italic = false });
                    i = j;
                    continue;
                }

                if (ch == '^' || ch == '_')
                {
                    bool isSup = ch == '^';
                    var argA = ReadAtom(s, i + 1);
                    var parsedArg = ParseLatexToBoxNodes(argA.content);
                    if (nodes.Count > 0 && nodes[^1].Kind is "text" or "scripted" or "accent" or "frac")
                    {
                        var prev = nodes[^1];
                        if (prev.Kind == "scripted")
                        {
                            if (isSup) prev.Sup = parsedArg; else prev.Sub = parsedArg;
                        }
                        else
                        {
                            nodes[^1] = new BoxNode { Kind = "scripted", BaseNodes = new List<BoxNode> { prev }, Sup = isSup ? parsedArg : null, Sub = !isSup ? parsedArg : null };
                        }
                    }
                    else
                    {
                        nodes.Add(new BoxNode { Kind = "scripted", BaseNodes = new List<BoxNode>(), Sup = isSup ? parsedArg : null, Sub = !isSup ? parsedArg : null });
                    }
                    i = argA.nextIdx;
                    continue;
                }

                if (ch == '{')
                {
                    var grp = ExtractBraceGroup(s, i, '{', '}');
                    if (grp.HasValue)
                    {
                        nodes.Add(new BoxNode { Kind = "group", Children = ParseLatexToBoxNodes(grp.Value.content) });
                        i = grp.Value.endIdx + 1;
                        continue;
                    }
                }

                if (char.IsWhiteSpace(ch)) { nodes.Add(new BoxNode { Kind = "space", SpaceW = 4 }); i++; continue; }

                if ("0123456789+-=<>()[]|.,:;!?/'\"".Contains(ch))
                {
                    string disp = ch == '-' ? "−" : (ch == '*' ? "·" : ch.ToString());
                    bool pad = "+-=<>".Contains(ch);
                    if (pad) nodes.Add(new BoxNode { Kind = "space", SpaceW = 3.5 });
                    nodes.Add(new BoxNode { Kind = "text", Text = disp, Italic = false });
                    if (pad) nodes.Add(new BoxNode { Kind = "space", SpaceW = 3.5 });
                }
                else
                {
                    nodes.Add(new BoxNode { Kind = "text", Text = ch.ToString(), Italic = char.IsLetter(ch) && ch <= 'z' });
                }
                i++;
            }
            return nodes;
        }

        private static BoxMetrics MeasureList(List<BoxNode>? nodes, double fontSize, double dpi)
        {
            if (nodes == null || nodes.Count == 0)
                return new BoxMetrics { W = 4, Ascent = fontSize * 0.72, Descent = fontSize * 0.25 };
            double w = 0, asc = fontSize * 0.72, desc = fontSize * 0.25;
            foreach (var nd in nodes)
            {
                var m = MeasureNode(nd, fontSize, dpi);
                nd.M = m;
                w += m.W;
                if (m.Ascent > asc) asc = m.Ascent;
                if (m.Descent > desc) desc = m.Descent;
            }
            return new BoxMetrics { W = w, Ascent = asc, Descent = desc };
        }

        private static BoxMetrics MeasureNode(BoxNode nd, double fontSize, double dpi)
        {
            if (nd.Kind == "space") return new BoxMetrics { W = nd.SpaceW, Ascent = fontSize * 0.5, Descent = fontSize * 0.1 };
            if (nd.Kind == "text")
            {
                var ft = MakeFt(nd.Text, fontSize, nd.Italic, Brushes.Black, dpi, nd.Env == "plain");
                return new BoxMetrics { W = ft.WidthIncludingTrailingWhitespace, Ascent = Math.Max(fontSize * 0.74, ft.Baseline), Descent = Math.Max(fontSize * 0.24, ft.Height - ft.Baseline) };
            }
            if (nd.Kind == "group") return MeasureList(nd.Children, fontSize, dpi);
            if (nd.Kind == "frac")
            {
                double subFs = Math.Max(11, Math.Round(fontSize * 0.80));
                var mNum = MeasureList(nd.Num, subFs, dpi);
                var mDen = MeasureList(nd.Den, subFs, dpi);
                nd.MNum = mNum; nd.MDen = mDen; nd.SubFs = subFs;
                double barW = Math.Max(mNum.W, mDen.W) + 12;
                double axisShift = fontSize * 0.26;
                return new BoxMetrics { W = barW + 4, Ascent = axisShift + mNum.Ascent + mNum.Descent + 4, Descent = Math.Max(fontSize * 0.3, (mDen.Ascent + mDen.Descent + 4) - axisShift) };
            }
            if (nd.Kind == "sqrt")
            {
                var mArg = MeasureList(nd.Arg, fontSize, dpi);
                double degFs = Math.Max(10, Math.Round(fontSize * 0.58));
                var mDeg = nd.Deg != null ? MeasureList(nd.Deg, degFs, dpi) : null;
                nd.MArg = mArg; nd.MDeg = mDeg; nd.DegFs = degFs;
                double radHeadW = fontSize * 0.62 + (mDeg != null ? Math.Max(0, mDeg.W - fontSize * 0.22) : 0);
                return new BoxMetrics { W = radHeadW + mArg.W + 6, Ascent = mArg.Ascent + 6, Descent = mArg.Descent + 2 };
            }
            if (nd.Kind == "bigop")
            {
                double opFs = Math.Round(fontSize * 1.65);
                var ftSym = MakeFt(nd.Sym, opFs, false, Brushes.Black, dpi);
                double symW = ftSym.WidthIncludingTrailingWhitespace;
                double limFs = Math.Max(10, Math.Round(fontSize * 0.62));
                var mSub = nd.Sub != null ? MeasureList(nd.Sub, limFs, dpi) : null;
                var mSup = nd.Sup != null ? MeasureList(nd.Sup, limFs, dpi) : null;
                nd.OpFs = opFs; nd.SymW = symW; nd.LimFs = limFs; nd.MSub = mSub; nd.MSup = mSup;
                if (nd.IsCenteredLimit)
                {
                    double totalW = Math.Max(symW, Math.Max(mSub?.W ?? 0, mSup?.W ?? 0)) + 6;
                    double asc = opFs * 0.52 + (mSup != null ? mSup.Ascent + mSup.Descent + 2 : 0);
                    double desc = opFs * 0.38 + (mSub != null ? mSub.Ascent + mSub.Descent + 2 : 0);
                    return new BoxMetrics { W = totalW, Ascent = asc, Descent = desc };
                }
                else
                {
                    double limW = Math.Max(mSub?.W ?? 0, mSup?.W ?? 0);
                    double asc = opFs * 0.58 + (mSup != null ? mSup.Ascent * 0.55 : 0);
                    double desc = opFs * 0.42 + (mSub != null ? mSub.Descent * 0.7 : 0);
                    return new BoxMetrics { W = symW + limW + 6, Ascent = asc, Descent = desc };
                }
            }
            if (nd.Kind == "lim")
            {
                double limW = MakeFt("lim", fontSize, false, Brushes.Black, dpi).WidthIncludingTrailingWhitespace;
                double subFs = Math.Max(10, Math.Round(fontSize * 0.62));
                var mSub = nd.Sub != null ? MeasureList(nd.Sub, subFs, dpi) : null;
                nd.LimW = limW; nd.SubFs = subFs; nd.MSub = mSub;
                return new BoxMetrics { W = Math.Max(limW, mSub?.W ?? 0) + 6, Ascent = fontSize * 0.75, Descent = fontSize * 0.28 + (mSub != null ? mSub.Ascent + mSub.Descent + 2 : 0) };
            }
            if (nd.Kind == "accent")
            {
                var mBody = MeasureList(nd.Body, fontSize, dpi);
                nd.MBody = mBody;
                return new BoxMetrics { W = mBody.W + 4, Ascent = mBody.Ascent + 8, Descent = mBody.Descent };
            }
            if (nd.Kind == "scripted")
            {
                var mBase = MeasureList(nd.BaseNodes, fontSize, dpi);
                double scrFs = Math.Max(10, Math.Round(fontSize * 0.65));
                var mSup = nd.Sup != null ? MeasureList(nd.Sup, scrFs, dpi) : null;
                var mSub = nd.Sub != null ? MeasureList(nd.Sub, scrFs, dpi) : null;
                nd.MBase = mBase; nd.ScrFs = scrFs; nd.MSup = mSup; nd.MSub = mSub;
                double scrW = Math.Max(mSup?.W ?? 0, mSub?.W ?? 0);
                double asc = Math.Max(mBase.Ascent, mSup != null ? fontSize * 0.42 + mSup.Ascent : 0);
                double desc = Math.Max(mBase.Descent, mSub != null ? fontSize * 0.22 + mSub.Descent : 0);
                return new BoxMetrics { W = mBase.W + scrW + 1, Ascent = asc, Descent = desc };
            }
            if (nd.Kind == "matrix" && nd.Rows != null)
            {
                double cellFs = Math.Max(12, Math.Round(fontSize * 0.86));
                int maxCols = nd.Rows.Count > 0 ? nd.Rows.Max(r => r.Count) : 1;
                var colWidths = new double[maxCols];
                var rowMetrics = new List<(List<BoxMetrics> cellsM, double rAsc, double rDesc)>();
                foreach (var row in nd.Rows)
                {
                    double rAsc = cellFs * 0.72, rDesc = cellFs * 0.28;
                    var cellsM = new List<BoxMetrics>();
                    for (int c = 0; c < row.Count; c++)
                    {
                        var cm = MeasureList(row[c], cellFs, dpi);
                        colWidths[c] = Math.Max(colWidths[c], cm.W);
                        if (cm.Ascent > rAsc) rAsc = cm.Ascent;
                        if (cm.Descent > rDesc) rDesc = cm.Descent;
                        cellsM.Add(cm);
                    }
                    rowMetrics.Add((cellsM, rAsc, rDesc));
                }
                nd.CellFs = cellFs; nd.ColWidths = colWidths; nd.RowMetrics = rowMetrics;
                double innerW = colWidths.Sum() + Math.Max(0, maxCols - 1) * 14;
                double totalH = rowMetrics.Sum(rm => rm.rAsc + rm.rDesc + 6);
                double braceW = nd.Env == "cases" ? 14 : (nd.Env is "pmatrix" or "bmatrix" ? 11 : 4);
                double rightW = nd.Env is "pmatrix" or "bmatrix" ? 11 : 4;
                nd.BraceW = braceW; nd.RightW = rightW; nd.TotalH = totalH;
                return new BoxMetrics { W = braceW + innerW + rightW + 6, Ascent = totalH / 2 + fontSize * 0.24, Descent = Math.Max(fontSize * 0.25, totalH / 2 - fontSize * 0.24) };
            }
            return new BoxMetrics { W = 6, Ascent = fontSize * 0.7, Descent = fontSize * 0.2 };
        }

        private static void DrawList(DrawingContext dc, List<BoxNode>? nodes, double x, double baselineY, double fontSize, Brush brush, Pen linePen, double dpi)
        {
            if (nodes == null) return;
            double curX = x;
            foreach (var nd in nodes)
            {
                DrawNode(dc, nd, curX, baselineY, fontSize, brush, linePen, dpi);
                curX += nd.M.W;
            }
        }

        private static void DrawNode(DrawingContext dc, BoxNode nd, double x, double baselineY, double fontSize, Brush brush, Pen linePen, double dpi)
        {
            if (nd.Kind == "space") return;
            if (nd.Kind == "text")
            {
                var ft = MakeFt(nd.Text, fontSize, nd.Italic, brush, dpi, nd.Env == "plain");
                dc.DrawText(ft, new Point(x, baselineY - ft.Baseline));
                return;
            }
            if (nd.Kind == "group") { DrawList(dc, nd.Children, x, baselineY, fontSize, brush, linePen, dpi); return; }
            if (nd.Kind == "frac" && nd.MNum != null && nd.MDen != null)
            {
                double barW = Math.Max(nd.MNum.W, nd.MDen.W) + 12;
                double axisY = baselineY - fontSize * 0.28;
                double numBaseY = axisY - 4 - nd.MNum.Descent;
                double denBaseY = axisY + 4 + nd.MDen.Ascent;
                DrawList(dc, nd.Num, x + 2 + (barW - nd.MNum.W) / 2.0, numBaseY, nd.SubFs, brush, linePen, dpi);
                var barPen = new Pen(brush, Math.Max(1.25, fontSize * 0.058));
                dc.DrawLine(barPen, new Point(x + 2, axisY), new Point(x + 2 + barW, axisY));
                DrawList(dc, nd.Den, x + 2 + (barW - nd.MDen.W) / 2.0, denBaseY, nd.SubFs, brush, linePen, dpi);
                return;
            }
            if (nd.Kind == "sqrt" && nd.MArg != null)
            {
                double topY = baselineY - nd.MArg.Ascent - 4;
                double botY = baselineY + nd.MArg.Descent;
                double degExtra = nd.MDeg != null ? Math.Max(0, nd.MDeg.W - fontSize * 0.22) : 0;
                double radStartX = x + 2 + degExtra;
                double radApexX = radStartX + fontSize * 0.26;
                double radTopX = radStartX + fontSize * 0.56;
                if (nd.MDeg != null) DrawList(dc, nd.Deg, x + 1, baselineY - nd.MArg.Ascent * 0.55, nd.DegFs, brush, linePen, dpi);
                var radPen = new Pen(brush, Math.Max(1.3, fontSize * 0.06)) { LineJoin = PenLineJoin.Miter };
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(radStartX, baselineY - nd.MArg.Ascent * 0.35), false, false);
                    ctx.LineTo(new Point(radApexX, botY), true, true);
                    ctx.LineTo(new Point(radTopX, topY), true, true);
                    ctx.LineTo(new Point(radTopX + nd.MArg.W + 4, topY), true, true);
                }
                g.Freeze();
                dc.DrawGeometry(null, radPen, g);
                DrawList(dc, nd.Arg, radTopX + 2, baselineY, fontSize, brush, linePen, dpi);
                return;
            }
            if (nd.Kind == "bigop")
            {
                var ft = MakeFt(nd.Sym, nd.OpFs, false, brush, dpi);
                double symBaseY = baselineY + nd.OpFs * 0.16;
                if (nd.IsCenteredLimit)
                {
                    double boxW = nd.M.W;
                    dc.DrawText(ft, new Point(x + (boxW - nd.SymW) / 2.0, symBaseY - ft.Baseline));
                    if (nd.MSup != null) DrawList(dc, nd.Sup, x + (boxW - nd.MSup.W) / 2.0, symBaseY - nd.OpFs * 0.72 - nd.MSup.Descent, nd.LimFs, brush, linePen, dpi);
                    if (nd.MSub != null) DrawList(dc, nd.Sub, x + (boxW - nd.MSub.W) / 2.0, symBaseY + nd.OpFs * 0.22 + nd.MSub.Ascent, nd.LimFs, brush, linePen, dpi);
                }
                else
                {
                    dc.DrawText(ft, new Point(x + 1, symBaseY - ft.Baseline));
                    double limX = x + nd.SymW + 3;
                    if (nd.MSup != null) DrawList(dc, nd.Sup, limX + 2, symBaseY - nd.OpFs * 0.62, nd.LimFs, brush, linePen, dpi);
                    if (nd.MSub != null) DrawList(dc, nd.Sub, limX, symBaseY + nd.OpFs * 0.24, nd.LimFs, brush, linePen, dpi);
                }
                return;
            }
            if (nd.Kind == "lim")
            {
                var ft = MakeFt("lim", fontSize, false, brush, dpi);
                double boxW = nd.M.W;
                dc.DrawText(ft, new Point(x + (boxW - nd.LimW) / 2.0, baselineY - ft.Baseline));
                if (nd.MSub != null) DrawList(dc, nd.Sub, x + (boxW - nd.MSub.W) / 2.0, baselineY + 3 + nd.MSub.Ascent, nd.SubFs, brush, linePen, dpi);
                return;
            }
            if (nd.Kind == "accent" && nd.MBody != null)
            {
                double bx = x + 2;
                DrawList(dc, nd.Body, bx, baselineY, fontSize, brush, linePen, dpi);
                double topY = baselineY - nd.MBody.Ascent - 3;
                var accPen = new Pen(brush, Math.Max(1.2, fontSize * 0.055));
                if (nd.Accent == "vec")
                {
                    dc.DrawLine(accPen, new Point(bx, topY), new Point(bx + nd.MBody.W, topY));
                    dc.DrawLine(accPen, new Point(bx + nd.MBody.W, topY), new Point(bx + nd.MBody.W - 4, topY - 3));
                    dc.DrawLine(accPen, new Point(bx + nd.MBody.W, topY), new Point(bx + nd.MBody.W - 4, topY + 3));
                }
                else if (nd.Accent == "hat")
                {
                    dc.DrawLine(accPen, new Point(bx, topY + 3), new Point(bx + nd.MBody.W / 2.0, topY - 2));
                    dc.DrawLine(accPen, new Point(bx + nd.MBody.W / 2.0, topY - 2), new Point(bx + nd.MBody.W, topY + 3));
                }
                else dc.DrawLine(accPen, new Point(bx, topY), new Point(bx + nd.MBody.W, topY));
                return;
            }
            if (nd.Kind == "scripted" && nd.MBase != null)
            {
                DrawList(dc, nd.BaseNodes, x, baselineY, fontSize, brush, linePen, dpi);
                double scrX = x + nd.MBase.W + 1;
                if (nd.MSup != null) DrawList(dc, nd.Sup, scrX, baselineY - fontSize * 0.42, nd.ScrFs, brush, linePen, dpi);
                if (nd.MSub != null) DrawList(dc, nd.Sub, scrX, baselineY + fontSize * 0.24, nd.ScrFs, brush, linePen, dpi);
                return;
            }
            if (nd.Kind == "matrix" && nd.Rows != null && nd.RowMetrics != null && nd.ColWidths != null)
            {
                double midY = baselineY - fontSize * 0.24;
                double topY = midY - nd.TotalH / 2.0;
                double botY = midY + nd.TotalH / 2.0;
                var bPen = new Pen(brush, 1.45);
                if (nd.Env == "cases")
                {
                    double bx = x + nd.BraceW - 3;
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(bx, topY), false, false);
                        ctx.QuadraticBezierTo(new Point(bx - 6, topY), new Point(bx - 6, midY - 4), true, false);
                        ctx.LineTo(new Point(bx - 10, midY), true, false);
                        ctx.LineTo(new Point(bx - 6, midY + 4), true, false);
                        ctx.QuadraticBezierTo(new Point(bx - 6, botY), new Point(bx, botY), true, false);
                    }
                    g.Freeze();
                    dc.DrawGeometry(null, bPen, g);
                }
                else if (nd.Env == "pmatrix")
                {
                    double lx = x + nd.BraceW - 2, rx = x + nd.M.W - nd.RightW + 2;
                    var gL = new StreamGeometry(); using (var ctx = gL.Open()) { ctx.BeginFigure(new Point(lx, topY), false, false); ctx.QuadraticBezierTo(new Point(lx - 8, midY), new Point(lx, botY), true, false); } gL.Freeze();
                    var gR = new StreamGeometry(); using (var ctx = gR.Open()) { ctx.BeginFigure(new Point(rx, topY), false, false); ctx.QuadraticBezierTo(new Point(rx + 8, midY), new Point(rx, botY), true, false); } gR.Freeze();
                    dc.DrawGeometry(null, bPen, gL); dc.DrawGeometry(null, bPen, gR);
                }
                else if (nd.Env == "bmatrix")
                {
                    double lx = x + nd.BraceW - 2, rx = x + nd.M.W - nd.RightW + 2;
                    dc.DrawLine(bPen, new Point(lx, topY), new Point(lx - 5, topY)); dc.DrawLine(bPen, new Point(lx - 5, topY), new Point(lx - 5, botY)); dc.DrawLine(bPen, new Point(lx - 5, botY), new Point(lx, botY));
                    dc.DrawLine(bPen, new Point(rx, topY), new Point(rx + 5, topY)); dc.DrawLine(bPen, new Point(rx + 5, topY), new Point(rx + 5, botY)); dc.DrawLine(bPen, new Point(rx + 5, botY), new Point(rx, botY));
                }

                double curY = topY;
                for (int r = 0; r < nd.Rows.Count; r++)
                {
                    var rm = nd.RowMetrics[r];
                    double rowBaseY = curY + 3 + rm.rAsc;
                    double cx = x + nd.BraceW + 3;
                    for (int c = 0; c < nd.Rows[r].Count; c++)
                    {
                        DrawList(dc, nd.Rows[r][c], cx, rowBaseY, nd.CellFs, brush, linePen, dpi);
                        cx += nd.ColWidths[c] + 14;
                    }
                    curY += rm.rAsc + rm.rDesc + 6;
                }
            }
        }

        private static List<(List<BoxNode> nodes, BoxMetrics metrics)> LayoutLines(string input, double fontSize, double dpi, bool forceMath)
        {
            var rawLines = (input ?? "").Replace("\r\n", "\n").Split('\n');
            var result = new List<(List<BoxNode>, BoxMetrics)>();
            foreach (var raw in rawLines)
            {
                var nodes = ParseMixedLine(raw, forceMath);
                var m = MeasureList(nodes, fontSize, dpi);
                result.Add((nodes, m));
            }
            return result;
        }

        public static (double width, double height) Measure(string input, double fontSize, double dpi, bool forceMath = false)
        {
            var lines = LayoutLines(input, fontSize, dpi, forceMath);
            if (lines.Count == 0) return (24, fontSize + 10);
            double maxW = Math.Max(20, lines.Max(l => l.metrics.W) + 12);
            double totalH = lines.Sum(l => l.metrics.Ascent + l.metrics.Descent + 6) + 6;
            return (maxW, Math.Max(fontSize + 10, totalH));
        }

        public static Rect Draw(DrawingContext dc, string input, Point topLeft, Color color, double fontSize, double dpi, bool forceMath = false)
        {
            var lines = LayoutLines(input, fontSize, dpi, forceMath);
            var brush = new SolidColorBrush(color);
            var pen = new Pen(brush, Math.Max(1.2, fontSize * 0.055));
            double curY = topLeft.Y + 4;
            double maxW = 20;

            foreach (var (nodes, m) in lines)
            {
                double baseY = curY + m.Ascent;
                DrawList(dc, nodes, topLeft.X + 6, baseY, fontSize, brush, pen, dpi);
                if (m.W + 12 > maxW) maxW = m.W + 12;
                curY += m.Ascent + m.Descent + 6;
            }
            return new Rect(topLeft.X, topLeft.Y, maxW, Math.Max(fontSize + 10, curY - topLeft.Y + 4));
        }

        public static RenderTargetBitmap RenderPreviewBitmap(string input, Color color, double fontSize, double scale = 2.0, bool forceMath = false)
        {
            var (w, h) = Measure(input, fontSize, 1.0, forceMath);
            int pw = Math.Max(24, (int)Math.Ceiling(w * scale));
            int ph = Math.Max(24, (int)Math.Ceiling(h * scale));
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                Draw(dc, input, new Point(0, 0), color, fontSize, 1.0, forceMath);
            }
            var rtb = new RenderTargetBitmap(pw, ph, 96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }
}
