// Barra de vida sobre la cabeza de las criaturas. Survivalcraft API 1.9 (juego 2.4).
// Un solo archivo (igual que Zombies.cs): se compila en el mismo dll o en uno propio.
//  - Solo se muestra si la criatura tiene la vida por debajo del maximo.
//  - Verde -> amarillo -> rojo segun la vida. Se oculta lejos y detras de bloques.
using System;
using System.Collections.Generic;
using Engine;
using Engine.Graphics;
using Engine.Media;
using GameEntitySystem;

namespace Game
{
    public class HealthBarsLoader : ModLoader
    {
        // ---- ajustes: cambia estos numeros a tu gusto ----
        const float Distancia = 15f;        // bloques; mas lejos no se dibuja
        const float AnchoMinimo = 0.9f;     // ancho minimo de la barra (bloques)
        const float Alto = 0.11f;           // grosor de la barra
        const float Separacion = 0.35f;     // altura sobre la cabeza
        const bool MostrarSiVidaLlena = true; 
        const bool MostrarAlJugador = false;
        const byte AlfaNormal = 128;        // 50% transparente
        const float SegundosOpaca = 3f;     // tras un golpe, 100% visible este tiempo
        const bool VerATravesDeBloques = true; // true: el agua y los bloques no tapan la barra
        const bool MostrarNumeros = true;   // vida en numeros: 15/25
        const float TamanoTexto = 0.011f;   // tamano de los numeros

        static readonly PrimitivesRenderer3D renderizador = new PrimitivesRenderer3D();
        SubsystemCreatureSpawn creaturas;
        static BitmapFont fuente;
        readonly Dictionary<ComponentCreature, float> vidaPrevia = new Dictionary<ComponentCreature, float>();
        readonly Dictionary<ComponentCreature, double> ultimoGolpe = new Dictionary<ComponentCreature, double>();
        double proximaLimpieza;
        SubsystemTime tiempoMundo;

        // al dormir el juego acelera el tiempo: las criaturas se mueven a lo loco y las barras con ellas.
        // Mientras el tiempo va acelerado se ocultan las barras y el panel.
        bool Durmiendo()
        {
            return tiempoMundo != null && tiempoMundo.GameTimeFactor > 1.01f;
        }

        public override void __ModInitialize()
        {
            ModsManager.RegisterHook("InitializeCreatureTypes", this);
            ModsManager.RegisterHook("GuiDraw", this);
            ModsManager.RegisterHook("ProcessAttackment", this);
            // GuiDraw se dibuja en el orden 9, ANTES que el agua (orden 100) y esta tapa la barra.
            // RenderModels se llama por cada orden de modelos (-10000, 1, 99, 201): usamos el
            // ultimo (201), que va despues del agua.
            ModsManager.RegisterHook("RenderModels", this);
        }

        // el juego llama a este hook una vez por cada mundo que se carga
        public override void InitializeCreatureTypes(
            SubsystemCreatureSpawn subsystemCreatureSpawn,
            List<SubsystemCreatureSpawn.CreatureType> creatureTypes)
        {
            creaturas = subsystemCreatureSpawn;
            tiempoMundo = subsystemCreatureSpawn.Project.FindSubsystem<SubsystemTime>(false);
            vidaPrevia.Clear();
            paneles.Clear();
            ultimoGolpe.Clear();
        }

        static bool desactivado;

        // ---- panel lateral del animal golpeado ----
        const float SegundosPanel = 10f;    // el panel dura 10 s tras el ultimo golpe a ese animal
        const float AnchoPanel = 200f;
        const float AltoPanel = 76f;
        const float AltoPanelCampeon = 98f; // con la linea extra del color del campeon
        const float AltoBarraPanel = 7f;    // grosor de la barrita fina

        class Panel
        {
            public CanvasWidget Caja;
            public LabelWidget Nombre;
            public LabelWidget Vida;
            public RectangleWidget Relleno;
            public RectangleWidget Fondo;
            public RectangleWidget FondoBarra;
            public LabelWidget Campeon;
            public int UltimoTipo = -2;
            public ComponentCreature Objetivo;
            public double Fin;
            public string UltimoTexto;
        }

        readonly Dictionary<ComponentGui, Panel> paneles = new Dictionary<ComponentGui, Panel>();
        static bool panelDesactivado;
        static System.Reflection.MemberInfo miembroObjetivo;
        static bool miembroBuscado;
        double ultimoAtaqueJugador = -100;
        ComponentCreature jugadorAtacante;

        static readonly string[] NombresCampeon = { "Rojo", "Amarillo", "Verde", "Azul", "Morado", "Dorado", "Negro", "Blanco", "Naranja", "Jefe" };
        static readonly Color[] ColoresCampeonUI =
        {
            new Color((byte)235, (byte)60, (byte)60, (byte)255),
            new Color((byte)255, (byte)230, (byte)60, (byte)255),
            new Color((byte)80, (byte)255, (byte)80, (byte)255),
            new Color((byte)90, (byte)140, (byte)255, (byte)255),
            new Color((byte)190, (byte)90, (byte)240, (byte)255),
            new Color((byte)255, (byte)205, (byte)70, (byte)255),
            new Color((byte)170, (byte)170, (byte)185, (byte)255),  // negro (gris claro para que se lea)
            new Color((byte)235, (byte)245, (byte)255, (byte)255),  // blanco
            new Color((byte)255, (byte)140, (byte)40, (byte)255),   // naranja
            new Color((byte)255, (byte)90, (byte)90, (byte)255)     // jefe
        };
        static readonly List<System.Reflection.MethodInfo> metodosTipo = new List<System.Reflection.MethodInfo>();
        static bool tipoBuscado;

        // pregunta a Campeones.cs (criaturas) y a Zombies.cs (zombis) de que color es el campeon; -1 si no lo es
        static int TipoCampeon(Entity e)
        {
            try
            {
                if (!tipoBuscado)
                {
                    tipoBuscado = true;
                    string[] clases = { "GiganteCode.CampeonesLoader", "Game.ZombiesLoader" };
                    foreach (System.Reflection.Assembly ens in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        foreach (string nombreCls in clases)
                        {
                            Type tipoCls = ens.GetType(nombreCls, false);
                            if (tipoCls == null) continue;
                            System.Reflection.MethodInfo m = tipoCls.GetMethod("TipoCampeon",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            if (m != null) metodosTipo.Add(m);
                        }
                    }
                }
                for (int i = 0; i < metodosTipo.Count; i++)
                {
                    int r = (int)metodosTipo[i].Invoke(null, new object[] { e });
                    if (r >= 0) return r;
                }
                return -1;
            }
            catch { return -1; }
        }

        // saca la criatura golpeada del Attackment (el campo "Target" cambia de tipo segun la version)
        static ComponentCreature CriaturaDe(object ataque)
        {
            if (ataque == null) return null;
            if (!miembroBuscado)
            {
                miembroBuscado = true;
                System.Reflection.BindingFlags f = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
                miembroObjetivo = (System.Reflection.MemberInfo)ataque.GetType().GetProperty("Target", f)
                    ?? (System.Reflection.MemberInfo)ataque.GetType().GetField("Target", f);
            }
            object o = null;
            System.Reflection.PropertyInfo p = miembroObjetivo as System.Reflection.PropertyInfo;
            System.Reflection.FieldInfo fi = miembroObjetivo as System.Reflection.FieldInfo;
            if (p != null) o = p.GetValue(ataque, null);
            else if (fi != null) o = fi.GetValue(ataque);
            return CriaturaDe(o as Component);
        }

        static ComponentCreature CriaturaDe(Component comp)
        {
            if (comp == null) return null;
            ComponentCreature c = comp as ComponentCreature;
            return c ?? comp.Entity.FindComponent<ComponentCreature>(false);
        }

        void RegistrarGolpe(ComponentCreature atacante, ComponentCreature objetivo)
        {
            if (panelDesactivado || atacante == null || objetivo == null) return;
            if (!(atacante is ComponentPlayer) || objetivo is ComponentPlayer) return;
            ComponentGui gui = atacante.Entity.FindComponent<ComponentGui>(false);
            if (gui == null) return;
            Panel p;
            if (!paneles.TryGetValue(gui, out p))
            {
                p = new Panel();
                paneles[gui] = p;
            }
            p.Objetivo = objetivo;
            p.Fin = Time.RealTime + SegundosPanel;
            p.UltimoTexto = null;
            p.UltimoTipo = -2;
        }

        public override void GuiDraw(ComponentGui componentGui, Camera camera, int drawOrder)
        {
            if (panelDesactivado || componentGui == null) return;
            try
            {
                Panel p;
                if (!paneles.TryGetValue(componentGui, out p)) return;
                ContainerWidget contenedor = componentGui.ControlsContainerWidget;
                if (contenedor == null) return;

                if (p.Caja == null)
                {
                    // al reaparecer el jugador el contenedor es el mismo: quita paneles viejos
                    List<ComponentGui> viejos = new List<ComponentGui>();
                    foreach (KeyValuePair<ComponentGui, Panel> par in paneles)
                    {
                        if (par.Key == componentGui || par.Value.Caja == null) continue;
                        if (par.Value.Caja.ParentWidget == contenedor)
                        {
                            contenedor.RemoveChildren(par.Value.Caja);
                            viejos.Add(par.Key);
                        }
                    }
                    for (int i = 0; i < viejos.Count; i++) paneles.Remove(viejos[i]);

                    p.Caja = new CanvasWidget();
                    p.Caja.Size = new Vector2(AnchoPanel, AltoPanel);
                    p.Caja.HorizontalAlignment = WidgetAlignment.Near;
                    p.Caja.VerticalAlignment = WidgetAlignment.Center;
                    p.Caja.Margin = new Vector2(8f, 0f);
                    p.Caja.IsHitTestVisible = false;

                    RectangleWidget fondo = new RectangleWidget();
                    fondo.Size = new Vector2(AnchoPanel, AltoPanel);
                    fondo.HorizontalAlignment = WidgetAlignment.Near;
                    fondo.VerticalAlignment = WidgetAlignment.Near;
                    fondo.FillColor = new Color((byte)0, (byte)0, (byte)0, (byte)150);
                    fondo.OutlineColor = new Color((byte)255, (byte)255, (byte)255, (byte)90);
                    fondo.OutlineThickness = 1f;
                    p.Caja.AddChildren(fondo);
                    p.Fondo = fondo;

                    StackPanelWidget col = new StackPanelWidget();
                    col.Direction = LayoutDirection.Vertical;
                    col.HorizontalAlignment = WidgetAlignment.Center;
                    col.VerticalAlignment = WidgetAlignment.Near;
                    col.Margin = new Vector2(0f, 6f);
                    p.Nombre = new LabelWidget();
                    p.Nombre.FontScale = 1.0f;
                    p.Nombre.Color = Color.White;
                    p.Nombre.HorizontalAlignment = WidgetAlignment.Center;
                    p.Vida = new LabelWidget();
                    p.Vida.FontScale = 1.0f;
                    p.Vida.HorizontalAlignment = WidgetAlignment.Center;
                    col.AddChildren(p.Nombre);
                    p.Campeon = new LabelWidget();
                    p.Campeon.FontScale = 0.8f;
                    p.Campeon.HorizontalAlignment = WidgetAlignment.Center;
                    p.Campeon.IsVisible = false;
                    col.AddChildren(p.Campeon);
                    col.AddChildren(p.Vida);
                    p.Caja.AddChildren(col);

                    // barrita fina abajo: fondo oscuro + relleno que cambia de ancho y color
                    float anchoBarra = AnchoPanel - 16f;
                    RectangleWidget fondoBarra = new RectangleWidget();
                    fondoBarra.Size = new Vector2(anchoBarra, AltoBarraPanel);
                    fondoBarra.HorizontalAlignment = WidgetAlignment.Near;
                    fondoBarra.VerticalAlignment = WidgetAlignment.Near;
                    fondoBarra.Margin = new Vector2(8f, AltoPanel - AltoBarraPanel - 8f);
                    fondoBarra.FillColor = new Color((byte)40, (byte)40, (byte)40, (byte)200);
                    fondoBarra.OutlineColor = Color.Transparent;
                    p.Caja.AddChildren(fondoBarra);
                    p.FondoBarra = fondoBarra;
                    p.Relleno = new RectangleWidget();
                    p.Relleno.Size = new Vector2(anchoBarra, AltoBarraPanel);
                    p.Relleno.HorizontalAlignment = WidgetAlignment.Near;
                    p.Relleno.VerticalAlignment = WidgetAlignment.Near;
                    p.Relleno.Margin = new Vector2(8f, AltoPanel - AltoBarraPanel - 8f);
                    p.Relleno.OutlineColor = Color.Transparent;
                    p.Caja.AddChildren(p.Relleno);
                    contenedor.AddChildren(p.Caja);
                }

                ComponentCreature c = p.Objetivo;
                bool visible = c != null && c.ComponentHealth != null && c.ComponentHealth.Health > 0f
                    && Time.RealTime < p.Fin && !Durmiendo();
                p.Caja.IsVisible = visible;
                if (!visible) return;

                // color del campeon (solo si existe Campeones.cs, juego 2.4)
                int tipo = TipoCampeon(c.Entity);
                if (tipo != p.UltimoTipo)
                {
                    p.UltimoTipo = tipo;
                    bool esCampeon = tipo >= 0 && tipo < NombresCampeon.Length;
                    float alto = esCampeon ? AltoPanelCampeon : AltoPanel;
                    Color borde = new Color((byte)255, (byte)255, (byte)255, (byte)90);
                    if (esCampeon)
                    {
                        Color cc = ColoresCampeonUI[tipo];
                        p.Campeon.Text = "Campeon: " + NombresCampeon[tipo];
                        p.Campeon.Color = cc;
                        borde = cc;
                    }
                    p.Campeon.IsVisible = esCampeon;
                    p.Caja.Size = new Vector2(AnchoPanel, alto);
                    p.Fondo.Size = new Vector2(AnchoPanel, alto);
                    p.Fondo.OutlineColor = borde;
                    p.Fondo.OutlineThickness = esCampeon ? 2f : 1f;
                    Vector2 mb = new Vector2(8f, alto - AltoBarraPanel - 8f);
                    p.FondoBarra.Margin = mb;
                    p.Relleno.Margin = mb;
                }

                float v = MathUtils.Clamp(c.ComponentHealth.Health, 0f, 1f);
                float max = c.ComponentHealth.AttackResilience;
                string vidaTxt = max > 0f
                    ? (v * max).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " / " + max.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                    : (v * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";
                if (vidaTxt != p.UltimoTexto)
                {
                    p.UltimoTexto = vidaTxt;
                    string nombre = c.DisplayName;
                    p.Nombre.Text = string.IsNullOrEmpty(nombre) ? "?" : nombre;
                    p.Vida.Text = vidaTxt;
                    Color colorVida = ColorVida(v, (byte)255);
                    p.Vida.Color = colorVida;
                    p.Relleno.Size = new Vector2(MathUtils.Max((AnchoPanel - 16f) * v, 1f), AltoBarraPanel);
                    p.Relleno.FillColor = colorVida;
                }
            }
            catch (Exception ex)
            {
                panelDesactivado = true;
                Log.Warning("Panel de vida desactivado por error: " + ex);
            }
        }

        // el golpe del jugador a una criatura abre/renueva el panel lateral
        public override void ProcessAttackment(Attackment attackment)
        {
            if (attackment == null || attackment.Attacker == null) return;
            try
            {
                ComponentCreature atq = attackment.Attacker.FindComponent<ComponentCreature>(false);
                if (atq is ComponentPlayer) { jugadorAtacante = atq; ultimoAtaqueJugador = Time.RealTime; }
                RegistrarGolpe(atq, CriaturaDe((object)attackment));
            }
            catch (Exception ex)
            {
                panelDesactivado = true;
                Log.Warning("Panel de vida desactivado por error: " + ex);
            }
        }


        public override void RenderModels(SubsystemModelsRenderer subsystemModelsRenderer, Camera camera,
            int drawOrder, bool skippedByOtherMods, out bool skip)
        {
            skip = false;
            if (desactivado || creaturas == null || camera == null || subsystemModelsRenderer == null) return;
            // solo en la ultima pasada de modelos (despues del agua)
            int[] ordenes = subsystemModelsRenderer.DrawOrders;
            if (ordenes == null || ordenes.Length == 0 || drawOrder != ordenes[ordenes.Length - 1]) return;
            try
            {
                Dibujar(camera);
            }
            catch (Exception e)
            {
                // si algo falla, el mod se apaga solo en vez de dejar la pantalla en negro
                desactivado = true;
                Log.Warning("BarraVida desactivada por error: " + e);
            }
        }

        void Dibujar(Camera camera)
        {
            if (Durmiendo()) return;

            Matrix vista = camera.ViewMatrix;
            Vector3 derecha = new Vector3(vista.M11, vista.M21, vista.M31);
            Vector3 arriba = new Vector3(vista.M12, vista.M22, vista.M32);
            Vector3 ojo = camera.ViewPosition;

            FlatBatch3D lote = renderizador.FlatBatch(
                100, (VerATravesDeBloques ? DepthStencilState.None : DepthStencilState.DepthRead), RasterizerState.CullNone, BlendState.AlphaBlend);

            FontBatch3D texto = null;
            if (MostrarNumeros)
            {
                if (fuente == null) fuente = ContentManager.Get<BitmapFont>("Fonts/Pericles");
                texto = renderizador.FontBatch(
                    fuente, 101, (VerATravesDeBloques ? DepthStencilState.None : DepthStencilState.DepthRead), RasterizerState.CullNone,
                    BlendState.AlphaBlend, SamplerState.LinearClamp);
            }

            double ahora = Time.RealTime;
            if (ahora > proximaLimpieza)
            {
                proximaLimpieza = ahora + 10.0;
                List<ComponentCreature> quitar = new List<ComponentCreature>();
                foreach (ComponentCreature k in vidaPrevia.Keys)
                    if (k.ComponentHealth == null || k.ComponentHealth.Health <= 0f) quitar.Add(k);
                foreach (ComponentCreature k in quitar) { vidaPrevia.Remove(k); ultimoGolpe.Remove(k); }
            }

            bool algo = false;
            foreach (ComponentCreature c in creaturas.Creatures)
            {
                if (c == null || c.ComponentBody == null || c.ComponentHealth == null) continue;
                float vida = c.ComponentHealth.Health;
                if (vida <= 0f) continue;
                if (!MostrarSiVidaLlena && vida >= 0.999f) continue;
                if (!MostrarAlJugador && c.Entity.FindComponent<ComponentPlayer>(false) != null) continue;

                float anterior;
                if (vidaPrevia.TryGetValue(c, out anterior) && vida < anterior - 0.0001f)
                {
                    ultimoGolpe[c] = ahora;
                    if (ahora - ultimoAtaqueJugador < 1.0 && jugadorAtacante != null && !(c is ComponentPlayer))
                        RegistrarGolpe(jugadorAtacante, c);
                }
                vidaPrevia[c] = vida;
                double golpe;
                bool recienGolpeada = ultimoGolpe.TryGetValue(c, out golpe) && ahora - golpe < SegundosOpaca;
                byte alfa = recienGolpeada ? (byte)255 : AlfaNormal;

                ComponentBody cuerpo = c.ComponentBody;
                Vector3 centro = cuerpo.Position + new Vector3(0f, cuerpo.BoxSize.Y + Separacion, 0f);
                if (Vector3.Distance(centro, ojo) > Distancia) continue;

                float ancho = Math.Max(AnchoMinimo, cuerpo.BoxSize.X * 1.2f);
                float mitad = ancho * 0.5f;
                float v = MathUtils.Clamp(vida, 0f, 1f);
                float h = Alto * 0.5f;

                // fondo oscuro
                Cuadro(lote, centro, derecha, arriba, -mitad - 0.02f, mitad + 0.02f, -h - 0.02f, h + 0.02f,
                    new Color((byte)0, (byte)0, (byte)0, (byte)(170 * alfa / 255)));
                // relleno
                Cuadro(lote, centro, derecha, arriba, -mitad, -mitad + ancho * v, -h, h, ColorVida(v, alfa));
                if (texto != null)
                {
                    float max = c.ComponentHealth.AttackResilience;
                    string etiqueta = max > 0f
                        ? (v * max).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "/" + max.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                        : (v * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";
                    // nombre de la criatura delante de los numeros: "Vaca 15/25"
                    string nombre = c.DisplayName;
                    if (!string.IsNullOrEmpty(nombre)) etiqueta = nombre + "  " + etiqueta;
                    texto.QueueText(etiqueta, centro + arriba * (h + 0.05f),
                        derecha * TamanoTexto, -arriba * TamanoTexto, new Color((byte)255, (byte)255, (byte)255, alfa),
                        TextAnchor.HorizontalCenter | TextAnchor.Bottom);
                }
                algo = true;
            }

            if (algo) renderizador.Flush(camera.ViewProjectionMatrix);
        }

        static void Cuadro(FlatBatch3D lote, Vector3 centro, Vector3 derecha, Vector3 arriba,
            float x0, float x1, float y0, float y1, Color color)
        {
            Vector3 a = centro + derecha * x0 + arriba * y0;
            Vector3 b = centro + derecha * x1 + arriba * y0;
            Vector3 c = centro + derecha * x1 + arriba * y1;
            Vector3 d = centro + derecha * x0 + arriba * y1;
            lote.QueueQuad(a, b, c, d, color);
        }

        static Color ColorVida(float v, byte alfa)
        {
            // 1 = verde, 0.5 = amarillo, 0 = rojo
            byte r = (byte)(255f * MathUtils.Clamp(2f - 2f * v, 0f, 1f));
            byte g = (byte)(255f * MathUtils.Clamp(2f * v, 0f, 1f));
            return new Color(r, g, (byte)30, alfa);
        }
    }

    // Boton "Correr": un boton en la pantalla (lado derecho) que alterna correr.
    //  - Correr = velocidad x1.7 y gasto de stamina x1.7 (solo el movimiento; el salto gasta igual).
    //  - Barra de stamina encima de los corazones y el hambre.
    //  - Boton "Buff": cada golpe cuerpo a cuerpo hace +3 de dano y gasta 16% de stamina.
    public class CorrerLoader : ModLoader
    {
        // ---- ajustes ----
        const float Multiplicador = 1.7f;          // velocidad al correr (1.7 = 70% mas rapido)
        const float GastoCorrerPorSegundo = 0.05f; // stamina extra que gasta correr por segundo mientras te mueves (0.05 = 5%)
        const string TextoApagado = "Correr";
        const string TextoEncendido = "Correr: ON";
        const bool MostrarBarraStamina = true;
        const float SeparacionBoton = 6f;          // espacio entre botones
        const float AltoBoton = 48f;
        // ---- buff de dano ----
        const string TextoBuffApagado = "Buff";
        const string TextoBuffEncendido = "Buff: ON";
        const float BonusMin = 1f;                 // dano extra minimo por golpe con el buff activo
        const float BonusMax = 3f;                 // dano extra maximo (sale al azar en pasos de 0.5: 1, 1.5, 2, 2.5, 3)
        static readonly System.Random azar = new System.Random();
        // ---- esquive y golpe fuerte ----
        const float CostoEsquive = 0.20f;          // stamina que gasta el esquive (0.20 = 20%)
        const float ImpulsoEsquive = 8f;           // fuerza del salto hacia adelante
        const double EsperaEsquive = 1.5;          // segundos entre esquives
        const float CostoFuerte = 0.40f;           // stamina que gasta el golpe fuerte (0.40 = 40%)
        const float MultiplicadorFuerte = 2f;      // el golpe fuerte hace x2 de dano
        const float AltoBotonChico = 40f;
        const float CostoStaminaGolpe = 0.16f;     // stamina que gasta cada golpe (0.16 = 16%)
        const float AltoBarra = 6f;                // grosor de la barra de stamina
        const float SeparacionBarra = 4f;          // espacio entre la barra y los corazones
        const float AnchoMinimoBarra = 80f;        // ancho minimo de la barra

        class Estado
        {
            public StackPanelWidget Panel;
            public BevelledButtonWidget Boton;
            public bool Corriendo;
            public float VelocidadBase;
            public double UltimoToque;
            public BevelledButtonWidget BotonBuff;
            public bool Buff;
            public double UltimoToqueBuff;
            public double UltimoAviso;
            public BevelledButtonWidget BotonEsquive;
            public BevelledButtonWidget BotonFuerte;
            public bool Fuerte;
            public double UltimoToqueEsquive;
            public double UltimoToqueFuerte;
            public double UltimoEsquive;
            public CanvasWidget Caja;
            public RectangleWidget Fondo;
            public RectangleWidget Relleno;
            public LabelWidget Etiqueta;    // texto: Stamina 73%
            public int UltimoPct = -1;
            public int UltimoAncho = -1;    // ancho (px) con el que se armo la barra
            public StackPanelWidget Fila;   // fila de segmentos de la barra
            public RectangleWidget[] Seg;   // segmentos (tamano fijo; solo cambia su color)
        }

        readonly Dictionary<ComponentGui, Estado> estados = new Dictionary<ComponentGui, Estado>();
        SubsystemTime tiempo;
        static bool desactivado;

        public override void __ModInitialize()
        {
            ModsManager.RegisterHook("InitializeCreatureTypes", this);
            ModsManager.RegisterHook("GuiDraw", this);
            ModsManager.RegisterHook("GuiUpdate", this);
            // en esta version del juego el golpe pasa por ProcessAttackment (el hook AttackBody ya no se llama)
            ModsManager.RegisterHook("ProcessAttackment", this);
        }

        public override void InitializeCreatureTypes(
            SubsystemCreatureSpawn subsystemCreatureSpawn,
            List<SubsystemCreatureSpawn.CreatureType> creatureTypes)
        {
            estados.Clear(); // mundo nuevo: se crean botones y barras nuevos
            tiempo = null;
        }

        // el juego gasta la stamina en ComponentVitalStats.UpdateStamina; aqui solo se resta el EXTRA
        // del movimiento (el salto no se toca), con las mismas formulas del juego
        public override void GuiUpdate(ComponentGui componentGui)
        {
            if (desactivado || componentGui == null) return;
            try
            {
                Estado e;
                if (!estados.TryGetValue(componentGui, out e) || !e.Corriendo) return;
                ComponentPlayer jugador = componentGui.Entity.FindComponent<ComponentPlayer>(false);
                if (jugador == null || !SupervivenciaActiva(jugador)) return;
                if (tiempo == null) tiempo = componentGui.Project.FindSubsystem<SubsystemTime>(true);
                float dt = tiempo.GameTimeDelta;
                if (dt <= 0f) return;

                ComponentVitalStats vital = jugador.ComponentVitalStats;
                ComponentLocomotion loc = jugador.ComponentLocomotion;
                ComponentBody cuerpo = jugador.ComponentBody;
                if (vital == null || loc == null || cuerpo == null) return;

                Vector2? orden = loc.LastWalkOrder;
                float caminar = orden.HasValue ? orden.Value.Length() : 0f;
                if (caminar <= 0f) return; // quieto: no hay gasto extra

                float alturaOjos = jugador.ComponentCreatureModel.EyePosition.Y - cuerpo.Position.Y;
                bool bajoAgua = cuerpo.ImmersionDepth > alturaOjos;
                bool nadando = cuerpo.ImmersionFactor > 0.33f && !cuerpo.StandingOnValue.HasValue;

                float k = 1f / MathUtils.Max(jugador.ComponentLevel.SpeedFactor, 0.75f);
                if (jugador.ComponentSickness.IsSick || jugador.ComponentFlu.HasFlu) k *= 5f;

                // parte del gasto del juego que depende del movimiento (por segundo)
                float movimiento = (nadando || bajoAgua)
                    ? 0.008f * caminar
                    : (0.07f + 0.006f * k) * caminar;

                vital.Stamina -= GastoCorrerPorSegundo * dt;
            }
            catch (Exception ex)
            {
                desactivado = true;
                Log.Warning("BotonCorrer (stamina) desactivado por error: " + ex);
            }
        }

        public override void GuiDraw(ComponentGui componentGui, Camera camera, int drawOrder)
        {
            if (desactivado || componentGui == null) return;
            try
            {
                ComponentPlayer jugador = componentGui.Entity.FindComponent<ComponentPlayer>(false);
                if (jugador == null || jugador.ComponentLocomotion == null) return;
                if (componentGui.ControlsContainerWidget == null) return;

                Estado e;
                if (!estados.TryGetValue(componentGui, out e))
                {
                    // Al morir el jugador reaparece con un ComponentGui nuevo, pero el contenedor de
                    // widgets es el mismo: quitamos el panel y la barra viejos para que no se encimen.
                    ContainerWidget contenedor = componentGui.ControlsContainerWidget;
                    List<ComponentGui> viejos = new List<ComponentGui>();
                    foreach (KeyValuePair<ComponentGui, Estado> par in estados)
                    {
                        if (par.Key == componentGui) continue;
                        Estado v = par.Value;
                        bool mismo = (v.Panel != null && v.Panel.ParentWidget == contenedor)
                            || (v.Caja != null && v.Caja.ParentWidget == contenedor);
                        if (!mismo) continue;
                        if (v.Panel != null) contenedor.RemoveChildren(v.Panel);
                        if (v.Caja != null) contenedor.RemoveChildren(v.Caja);
                        viejos.Add(par.Key);
                    }
                    for (int i = 0; i < viejos.Count; i++) estados.Remove(viejos[i]);

                    e = new Estado();
                    e.Panel = new StackPanelWidget();
                    e.Panel.Direction = LayoutDirection.Vertical;
                    e.Panel.HorizontalAlignment = WidgetAlignment.Far;
                    e.Panel.VerticalAlignment = WidgetAlignment.Center;
                    e.Panel.Margin = new Vector2(8f, 0f);

                    e.Boton = new BevelledButtonWidget();
                    e.Boton.Text = TextoApagado;
                    e.Boton.Size = new Vector2(96f, AltoBoton);
                    e.Boton.Margin = new Vector2(0f, SeparacionBoton * 0.5f);
                    e.Panel.AddChildren(e.Boton);

                    e.BotonBuff = new BevelledButtonWidget();
                    e.BotonBuff.Text = TextoBuffApagado;
                    e.BotonBuff.Size = new Vector2(96f, AltoBoton);
                    e.BotonBuff.Margin = new Vector2(0f, SeparacionBoton * 0.5f);
                    e.Panel.AddChildren(e.BotonBuff);

                    e.BotonEsquive = new BevelledButtonWidget();
                    e.BotonEsquive.Text = "Esquive";
                    e.BotonEsquive.Size = new Vector2(96f, AltoBotonChico);
                    e.BotonEsquive.Margin = new Vector2(0f, SeparacionBoton * 0.5f);
                    e.Panel.AddChildren(e.BotonEsquive);

                    e.BotonFuerte = new BevelledButtonWidget();
                    e.BotonFuerte.Text = "Fuerte";
                    e.BotonFuerte.Size = new Vector2(96f, AltoBotonChico);
                    e.BotonFuerte.Margin = new Vector2(0f, SeparacionBoton * 0.5f);
                    e.Panel.AddChildren(e.BotonFuerte);

                    e.Etiqueta = new LabelWidget();
                    e.Etiqueta.Text = "Stamina";
                    e.Etiqueta.HorizontalAlignment = WidgetAlignment.Center;
                    e.Panel.AddChildren(e.Etiqueta);

                    componentGui.ControlsContainerWidget.AddChildren(e.Panel);

                    if (MostrarBarraStamina)
                    {
                        e.Caja = new CanvasWidget();
                        e.Caja.HorizontalAlignment = WidgetAlignment.Near;
                        e.Caja.VerticalAlignment = WidgetAlignment.Near;
                        e.Caja.IsHitTestVisible = false;
                        // el fondo y los segmentos se arman en ActualizarBarra (ArmarBarra)
                        e.Caja.IsVisible = false;
                        componentGui.ControlsContainerWidget.AddChildren(e.Caja);
                    }
                    estados[componentGui] = e;
                }

                ComponentLocomotion loc = jugador.ComponentLocomotion;
                double ahora = Time.RealTime;
                if (e.Boton.IsClicked && ahora - e.UltimoToque > 0.25)
                {
                    e.UltimoToque = ahora;
                    e.Corriendo = !e.Corriendo;
                    e.Boton.Text = e.Corriendo ? TextoEncendido : TextoApagado;
                    if (e.Corriendo) e.VelocidadBase = loc.WalkSpeed;
                    else loc.WalkSpeed = e.VelocidadBase;
                }
                if (e.Corriendo) loc.WalkSpeed = e.VelocidadBase * Multiplicador;

                if (e.BotonBuff.IsClicked && ahora - e.UltimoToqueBuff > 0.25)
                {
                    e.UltimoToqueBuff = ahora;
                    e.Buff = !e.Buff;
                    e.BotonBuff.Text = e.Buff ? TextoBuffEncendido : TextoBuffApagado;
                }

                if (e.BotonFuerte.IsClicked && ahora - e.UltimoToqueFuerte > 0.25)
                {
                    e.UltimoToqueFuerte = ahora;
                    e.Fuerte = !e.Fuerte;
                    e.BotonFuerte.Text = e.Fuerte ? "Fuerte: ON" : "Fuerte";
                }
                if (e.BotonEsquive.IsClicked && ahora - e.UltimoToqueEsquive > 0.25)
                {
                    e.UltimoToqueEsquive = ahora;
                    Esquivar(componentGui, jugador, e, ahora);
                }

                // porcentaje de stamina en texto (el texto del juego siempre se refresca)
                if (jugador.ComponentVitalStats != null && e.Etiqueta != null)
                {
                    int pct = (int)Math.Round(MathUtils.Clamp(jugador.ComponentVitalStats.Stamina, 0f, 1f) * 100f);
                    if (pct != e.UltimoPct)
                    {
                        e.UltimoPct = pct;
                        e.Etiqueta.Text = "Stamina " + pct + "%";
                    }
                }

                ColocarBoton(componentGui, e);
                if (e.Caja != null) ActualizarBarra(componentGui, jugador, e);
            }
            catch (Exception ex)
            {
                desactivado = true;
                Log.Warning("BotonCorrer desactivado por error: " + ex);
            }
        }

        // busca el boton de agacharse (en cualquier parte de la pantalla del jugador)
        static ButtonWidget BuscarAgachar(ComponentGui gui)
        {
            ButtonWidget b = null;
            ContainerWidget contenedor = gui.ControlsContainerWidget;
            ContainerWidget raiz = contenedor.RootWidget as ContainerWidget;
            if (raiz != null) b = raiz.Children.Find<ButtonWidget>("SneakButton", false);
            if (b == null) b = contenedor.Children.Find<ButtonWidget>("SneakButton", false);
            return b;
        }

        // pone el panel (Correr + Buff) en la columna derecha, justo debajo del boton de agacharse
        // (si no lo encuentra, se queda a media altura en el borde derecho)
        void ColocarBoton(ComponentGui gui, Estado e)
        {
            ContainerWidget contenedor = gui.ControlsContainerWidget;
            ButtonWidget agachar = BuscarAgachar(gui);
            if (agachar == null || !agachar.IsVisibleGlobal) return;
            float escala = contenedor.GlobalScale > 0.0001f ? contenedor.GlobalScale : 1f;
            BoundingRectangle area = contenedor.GlobalBounds;
            BoundingRectangle b = agachar.GlobalBounds;
            e.Panel.HorizontalAlignment = WidgetAlignment.Far;
            e.Panel.VerticalAlignment = WidgetAlignment.Near;
            e.Panel.Margin = new Vector2(
                MathUtils.Max((area.Max.X - b.Max.X) / escala, 0f),
                (b.Max.Y - area.Min.Y) / escala + SeparacionBoton * 0.5f);
        }

        // esquive: salto rapido hacia adelante, gasta stamina y tiene espera
        void Esquivar(ComponentGui gui, ComponentPlayer jugador, Estado e, double ahora)
        {
            if (ahora - e.UltimoEsquive < EsperaEsquive) return;
            ComponentBody cuerpo = jugador.ComponentBody;
            if (cuerpo == null) return;
            ComponentVitalStats vital = jugador.ComponentVitalStats;
            if (vital != null && SupervivenciaActiva(jugador))
            {
                if (vital.Stamina < CostoEsquive)
                {
                    gui.DisplaySmallMessage("Sin stamina para esquivar", Color.White, true, false);
                    return;
                }
                vital.Stamina -= CostoEsquive;
            }
            Vector3 adelante = cuerpo.Matrix.Forward;
            adelante.Y = 0f;
            if (adelante.Length() < 0.01f) return;
            adelante = Vector3.Normalize(adelante);
            cuerpo.Velocity = cuerpo.Velocity + adelante * ImpulsoEsquive + new Vector3(0f, 1.5f, 0f);
            e.UltimoEsquive = ahora;
        }

        // buff: cada golpe cuerpo a cuerpo del jugador hace +BonusDanio y gasta CostoStaminaGolpe de stamina.
        // golpe fuerte: el siguiente golpe hace x2 de dano y gasta CostoFuerte (se apaga solo al usarlo).
        // sin stamina suficiente (en supervivencia) el golpe sale normal, sin bonus y sin gasto.
        public override void ProcessAttackment(Attackment attackment)
        {
            if (desactivado || attackment == null || !(attackment is MeleeAttackment) || attackment.AttackPower <= 0f) return;
            try
            {
                if (attackment.Attacker == null) return;
                ComponentPlayer jugador = attackment.Attacker.FindComponent<ComponentPlayer>(false);
                if (jugador == null) return;
                ComponentGui gui = jugador.Entity.FindComponent<ComponentGui>(false);
                Estado e;
                if (gui == null || !estados.TryGetValue(gui, out e) || (!e.Buff && !e.Fuerte)) return;

                ComponentVitalStats vital = jugador.ComponentVitalStats;
                bool gasta = vital != null && SupervivenciaActiva(jugador);
                double ahora = Time.RealTime;

                if (e.Buff)
                {
                    if (gasta && vital.Stamina < CostoStaminaGolpe)
                    {
                        if (ahora - e.UltimoAviso > 2.0)
                        {
                            e.UltimoAviso = ahora;
                            gui.DisplaySmallMessage("Sin stamina para el buff", Color.White, true, false);
                        }
                    }
                    else
                    {
                        if (gasta) vital.Stamina -= CostoStaminaGolpe;
                        int pasos = (int)Math.Round((BonusMax - BonusMin) / 0.5f);
                        attackment.AttackPower += BonusMin + 0.5f * azar.Next(pasos + 1);
                    }
                }

                if (e.Fuerte)
                {
                    if (gasta && vital.Stamina < CostoFuerte)
                    {
                        if (ahora - e.UltimoAviso > 2.0)
                        {
                            e.UltimoAviso = ahora;
                            gui.DisplaySmallMessage("Sin stamina para el golpe fuerte", Color.White, true, false);
                        }
                    }
                    else
                    {
                        if (gasta) vital.Stamina -= CostoFuerte;
                        attackment.AttackPower *= MultiplicadorFuerte;
                        e.Fuerte = false; // se usa una sola vez
                        e.BotonFuerte.Text = "Fuerte";
                    }
                }
            }
            catch (Exception ex)
            {
                desactivado = true;
                Log.Warning("Buff de dano desactivado por error: " + ex);
            }
        }

        // la stamina solo se gasta en supervivencia (igual que el juego)
        static bool SupervivenciaActiva(ComponentPlayer jugador)
        {
            SubsystemGameInfo info = jugador.Project.FindSubsystem<SubsystemGameInfo>(true);
            return (int)info.WorldSettings.GameMode >= (int)GameMode.Survival
                && info.WorldSettings.AreAdventureSurvivalMechanicsEnabled;
        }

        // pone la barra justo encima de los corazones y el hambre y la rellena segun la stamina
        void ActualizarBarra(ComponentGui gui, ComponentPlayer jugador, Estado e)
        {
            ValueBarWidget vida = gui.HealthBarWidget;
            ContainerWidget contenedor = gui.ControlsContainerWidget;
            ComponentVitalStats vital = jugador.ComponentVitalStats;
            bool mostrar = vida != null && vital != null && vida.IsVisibleGlobal && SupervivenciaActiva(jugador);
            e.Caja.IsVisible = mostrar;
            if (!mostrar) return;

            // zona que ocupan los corazones (y el hambre, si se ve), en pixeles de pantalla
            BoundingRectangle zona = vida.GlobalBounds;
            ValueBarWidget comida = gui.FoodBarWidget;
            if (comida != null && comida.IsVisibleGlobal)
            {
                BoundingRectangle c = comida.GlobalBounds;
                zona = new BoundingRectangle(
                    new Vector2(MathUtils.Min(zona.Min.X, c.Min.X), MathUtils.Min(zona.Min.Y, c.Min.Y)),
                    new Vector2(MathUtils.Max(zona.Max.X, c.Max.X), MathUtils.Max(zona.Max.Y, c.Max.Y)));
            }

            float escala = contenedor.GlobalScale > 0.0001f ? contenedor.GlobalScale : 1f;
            BoundingRectangle area = contenedor.GlobalBounds;
            float ancho = MathUtils.Max((zona.Max.X - zona.Min.X) / escala, AnchoMinimoBarra);
            float x = (zona.Min.X - area.Min.X) / escala;
            float y = (zona.Min.Y - area.Min.Y) / escala - AltoBarra - SeparacionBarra;
            if (y < 0f) y = 0f;

            float v = MathUtils.Clamp(vital.Stamina, 0f, 1f);
            e.Caja.Margin = new Vector2(x, y);
            e.Caja.Size = new Vector2(ancho, AltoBarra);

            // Los segmentos tienen tamano fijo (no dependen de recalcular el layout): cada cuadro solo
            // se cambia el COLOR de cada segmento, que el juego lee en cada dibujado.
            int anchoPx = (int)Math.Round(ancho);
            if (anchoPx != e.UltimoAncho || e.Seg == null)
            {
                ArmarBarra(e, ancho);
                e.UltimoAncho = anchoPx;
            }
            int n = e.Seg.Length;
            int encendidos = (int)Math.Ceiling(v * n - 0.01f);
            Color color = ColorStamina(v);
            Color apagado = new Color((byte)40, (byte)40, (byte)40, (byte)90);
            for (int i = 0; i < n; i++)
                e.Seg[i].FillColor = i < encendidos ? color : apagado;
        }

        const int Segmentos = 20;

        // arma (o rearma si cambia el ancho) el fondo y la fila de segmentos dentro de la caja
        static void ArmarBarra(Estado e, float ancho)
        {
            if (e.Fila != null) e.Caja.RemoveChildren(e.Fila);
            if (e.Fondo != null) e.Caja.RemoveChildren(e.Fondo);
            e.Fondo = NuevoRect(ancho, AltoBarra,
                new Color((byte)0, (byte)0, (byte)0, (byte)150), new Color((byte)255, (byte)255, (byte)255, (byte)90), 1f);
            e.Caja.AddChildren(e.Fondo);

            e.Fila = new StackPanelWidget();
            e.Fila.Direction = LayoutDirection.Horizontal;
            e.Fila.HorizontalAlignment = WidgetAlignment.Near;
            e.Fila.VerticalAlignment = WidgetAlignment.Near;
            e.Seg = new RectangleWidget[Segmentos];
            float w = ancho / Segmentos;
            for (int i = 0; i < Segmentos; i++)
            {
                RectangleWidget s = NuevoRect(w, AltoBarra, new Color((byte)40, (byte)40, (byte)40, (byte)90), Color.Transparent, 0f);
                e.Seg[i] = s;
                e.Fila.AddChildren(s);
            }
            e.Caja.AddChildren(e.Fila);
        }

        static RectangleWidget NuevoRect(float ancho, float alto, Color relleno, Color borde, float grosorBorde)
        {
            RectangleWidget r = new RectangleWidget();
            r.HorizontalAlignment = WidgetAlignment.Near;
            r.VerticalAlignment = WidgetAlignment.Near;
            r.Size = new Vector2(ancho, alto);
            r.FillColor = relleno;
            r.OutlineColor = borde;
            r.OutlineThickness = grosorBorde;
            return r;
        }

        static Color ColorStamina(float v)
        {
            if (v < 0.1f) return new Color((byte)220, (byte)60, (byte)50, (byte)255);    // casi agotada
            if (v < 0.33f) return new Color((byte)240, (byte)150, (byte)40, (byte)255);  // cansado
            return new Color((byte)80, (byte)190, (byte)255, (byte)255);                // normal
        }
    }
}
