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

        public override void __ModInitialize()
        {
            ModsManager.RegisterHook("InitializeCreatureTypes", this);
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
            vidaPrevia.Clear();
            ultimoGolpe.Clear();
        }

        static bool desactivado;

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
                if (vidaPrevia.TryGetValue(c, out anterior) && vida < anterior - 0.0001f) ultimoGolpe[c] = ahora;
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
                        ? ((int)Math.Ceiling(v * max)).ToString() + "/" + ((int)Math.Round(max)).ToString()
                        : ((int)Math.Ceiling(v * 100f)).ToString() + "%";
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
    //  - Boton "Buff": cada golpe cuerpo a cuerpo hace +3 de dano y gasta 10% de stamina.
    public class CorrerLoader : ModLoader
    {
        // ---- ajustes ----
        const float Multiplicador = 1.7f;          // velocidad al correr (1.7 = 70% mas rapido)
        const float MultiplicadorStamina = 1.7f;   // gasto de stamina al correr (1.7 = 70% mas)
        const string TextoApagado = "Correr";
        const string TextoEncendido = "Correr: ON";
        const bool MostrarBarraStamina = true;
        const float SeparacionBoton = 6f;          // espacio entre botones
        const float AltoBoton = 48f;
        // ---- buff de dano ----
        const string TextoBuffApagado = "Buff";
        const string TextoBuffEncendido = "Buff: ON";
        const float BonusDanio = 3f;               // dano extra por golpe con el buff activo
        const float CostoStaminaGolpe = 0.10f;     // stamina que gasta cada golpe (0.10 = 10%)
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
            public CanvasWidget Caja;
            public RectangleWidget Fondo;
            public RectangleWidget Relleno;
            public int UltimoAncho = -1;    // ancho (px) con el que se armo el fondo
            public int UltimoRelleno = -1;  // ancho (px) con el que se armo el relleno
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

                vital.Stamina -= (MultiplicadorStamina - 1f) * movimiento * dt;
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

                    componentGui.ControlsContainerWidget.AddChildren(e.Panel);

                    if (MostrarBarraStamina)
                    {
                        e.Caja = new CanvasWidget();
                        e.Caja.HorizontalAlignment = WidgetAlignment.Near;
                        e.Caja.VerticalAlignment = WidgetAlignment.Near;
                        e.Caja.IsHitTestVisible = false;
                        e.Fondo = new RectangleWidget();
                        e.Fondo.HorizontalAlignment = WidgetAlignment.Near;
                        e.Fondo.VerticalAlignment = WidgetAlignment.Near;
                        e.Fondo.FillColor = new Color((byte)0, (byte)0, (byte)0, (byte)150);
                        e.Fondo.OutlineColor = new Color((byte)255, (byte)255, (byte)255, (byte)90);
                        e.Fondo.OutlineThickness = 1f;
                        e.Relleno = new RectangleWidget();
                        e.Relleno.HorizontalAlignment = WidgetAlignment.Near;
                        e.Relleno.VerticalAlignment = WidgetAlignment.Near;
                        e.Relleno.OutlineColor = Color.Transparent;
                        e.Caja.AddChildren(e.Fondo);
                        e.Caja.AddChildren(e.Relleno);
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

        // buff: cada golpe cuerpo a cuerpo del jugador hace +BonusDanio y gasta CostoStaminaGolpe de stamina.
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
                if (gui == null || !estados.TryGetValue(gui, out e) || !e.Buff) return;

                ComponentVitalStats vital = jugador.ComponentVitalStats;
                if (vital != null && SupervivenciaActiva(jugador))
                {
                    if (vital.Stamina < CostoStaminaGolpe)
                    {
                        double ahora = Time.RealTime;
                        if (ahora - e.UltimoAviso > 2.0)
                        {
                            e.UltimoAviso = ahora;
                            gui.DisplaySmallMessage("Sin stamina para el buff", Color.White, true, false);
                        }
                        return;
                    }
                    vital.Stamina -= CostoStaminaGolpe;
                }
                attackment.AttackPower += BonusDanio;
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

            // RectangleWidget.Size es una propiedad automatica: al cambiarla el juego NO recalcula el
            // layout y el relleno se quedaba con el ancho del primer cuadro. Por eso se recrean los
            // rectangulos cuando cambia su ancho en pixeles.
            int anchoPx = (int)Math.Round(ancho);
            int rellenoPx = (int)Math.Round(ancho * v);
            Color color = ColorStamina(v);
            if (anchoPx != e.UltimoAncho)
            {
                e.Caja.RemoveChildren(e.Relleno);
                e.Caja.RemoveChildren(e.Fondo);
                e.Fondo = NuevoRect(ancho, AltoBarra,
                    new Color((byte)0, (byte)0, (byte)0, (byte)150), new Color((byte)255, (byte)255, (byte)255, (byte)90), 1f);
                e.Caja.AddChildren(e.Fondo);
                e.Relleno = null;
                e.UltimoAncho = anchoPx;
                e.UltimoRelleno = -1;
            }
            if (rellenoPx != e.UltimoRelleno || e.Relleno == null)
            {
                if (e.Relleno != null) e.Caja.RemoveChildren(e.Relleno);
                e.Relleno = NuevoRect(ancho * v, AltoBarra, color, Color.Transparent, 0f);
                e.Caja.AddChildren(e.Relleno);
                e.UltimoRelleno = rellenoPx;
            }
            e.Relleno.FillColor = color;
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
