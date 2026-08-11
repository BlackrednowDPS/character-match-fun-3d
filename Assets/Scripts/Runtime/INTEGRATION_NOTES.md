# Runtime Integration Notes

## Assumptions

- `MatchShape`: enum con `Line`, `LShape` y `TShape`. Pillar 7 sólo necesita distinguir la geometría que modifica el ataque del jefe; no añadí orientación ni coordenadas porque el grid ya conserva esa información.
- `MatchContext`: `readonly struct` con posición de origen en mundo, centro lógico del tablero, cantidad de piezas y unión de `ElementalTag`. Es un valor pequeño y sin referencias mutables para pasarlo a `ReactionExecutor` sin crear objetos por match.
- `HitZone`: enum con `Body`, `Shell` y `WeakPoint`. Representa las tres clases mínimas necesarias para aplicar multiplicadores normal, reducido y aumentado sin acoplar el cálculo puro a colliders o GameObjects.
- `BossProfile`: `ScriptableObject` con HP máximo, daño base del jugador, arreglo ordenado de `BossPhase` y arreglo de `CounterAttackSO`. Se eligió como asset porque son datos de balance compartidos y Pillar 7 lo consume como perfil autorado.
- `BossPhase`: clase `[Serializable]` embebida en `BossProfile`, con umbral normalizado de HP, temporizador de enrage y `HitZone` activa. No es otro asset porque una fase sólo tiene sentido dentro de un perfil y debe conservar un orden explícito.
- `SeasonType`: enum de cuatro valores (`Spring`, `Summer`, `Fall`, `Winter`). El hemisferio y la fecha se resuelven fuera del valor para que los eventos del bus sigan siendo pequeños.
- `HolidayEvent`: enum con `None` y los dos ejemplos concretos de los pilares (`HalloweenJackOLanternEgg`, `WinterSantaYolk`). La ventana y región no viven en el enum; son datos del calendario para no convertir fechas de live-ops en código.
- `ReactionRecipeSO`: `ScriptableObject` con ID estable, dos componentes `ElementalTag`, `ReactionType`, prioridad y cantidad mínima para recetas del mismo tag. Esto permite ordenar una vez, balancear en assets y representar recetas como Frozen+Frozen sin una API paralela.
- `CounterAttackSO`: `ScriptableObject` con tipo de ataque, cooldown, fase mínima y parámetros para scramble, lock o hazard. Un solo formato cubre las tres variantes de Pillar 2 y deja sus números fuera del scheduler.
- `RecipeBookSaveData`: clase `[Serializable]` con arreglo de `RecipeBookEntry`; cada entrada conserva ID, componentes, descubrimiento, primera fecha UTC y clave de texto. El arreglo se prepara antes de evaluar matches y `Record` es idempotente para evitar crecimiento por descubrimientos repetidos.
- `ProjectilePool`: componente concreto y sellado derivado de `ComponentObjectPool<BossProjectile>`. La clase existe para que Unity pueda añadir y serializar un pool concreto en el Inspector aunque la implementación reusable sea genérica.
- `FractureShard`: `MonoBehaviour` que implementa `IPoolable`, cachea `Rigidbody`, renderers, colores y `MaterialPropertyBlock`, y recuerda temporalmente su `FractureShardPool` propietario. Es la unidad física/visual mínima que el motor puede impulsar, desvanecer y devolver sin `Instantiate`, `Destroy` ni `renderer.material` durante el juego.

## Needs From Claude

- Prefabs de pieza para el `EggPiecePool`: `EggPiece`, Collider y, si se desea física de clima/scramble, `Rigidbody`; su nombre/arquetipo visual debe corresponder a Hardboiled, Fried o Scrambled.
- Prefab de proyectil con `BossProjectile`, Collider configurado como trigger y `Rigidbody`, más un `ProjectilePool` con capacidad suficiente para la ráfaga y cascadas previstas.
- Prefabs de shards prefracturados y sus `FractureShardPool` para cada combinación usada de `Fracture_<Archetype>_<Light|Medium|Shatter>`. Cada shard necesita `FractureShard`, `Rigidbody` y sus `MeshRenderer`.
- Assets `ReactionRecipeSO`, `BossProfile` y `CounterAttackSO` con IDs, prioridades, fases, umbrales, cooldowns y parámetros de efecto autorados. También debe existir el asset de diálogo que usa `StressDetector`; cualquier calendario/asset cosmético pertenece a su integración separada y no se implementa como parte de esta revisión.
- Materiales de shards preparados para transparencia o dithering. El código sólo cambia `_BaseColor`/`_Color` mediante `MaterialPropertyBlock`; no puede convertir un material URP opaco en transparente.
- Layers dedicadas para las piezas seleccionables y, de ser necesario, los hit zones/proyectiles; asignar `pieceLayerMask` y `matchTargetLayerMask` en el Inspector. El código no exige tags de gameplay. La cámara usada como fallback de `Camera.main` sí necesita el tag estándar `MainCamera`.
- En Project Settings, habilitar una modalidad de Active Input Handling que incluya el Input Manager legado, porque estos scripts usan `UnityEngine.Input` para touch, mouse, teclado, gyro, acelerómetro y ubicación. Conceder acceso de red y configurar permisos/textos de ubicación por plataforma móvil; verificar también que el módulo de física 3D esté incluido.
- GameObjects de escena, además de `GridAnchor`, `BossSpawnPoint` y `StageRoot`, que hospeden y conecten `GridManager3D`, `EggPiecePool`, `CulinaryReactionSystem`, `MeshFractureEngine`, `GyroGravityController`, `StressDetector`, `WeatherSyncManager`, `BossBattleManager` y `ProjectilePool`; una cámara; colliders `BossHitZone`; raíces opcionales para pools; y un MonoBehaviour que implemente `IPlayerAssistSystem`.
- Wiring de Inspector entre managers, pools, prefabs, cámara, máscaras, perfil del jefe y base de recetas; llamada de bootstrap a `GridManager3D.Initialize(seed)` e `InitializeEncounter(profile)` cuando corresponda.
- Adaptador de guardado real para `RecipeBookSaveData`. `JsonUtility` no conserva `DateTime` de forma portable, por lo que `FirstDiscoveredUtc` debe mapearse a un string o entero estable en el formato de guardado del proyecto.

## Known Risks

- No se compiló ni se abrió Unity, por instrucción. La revisión confirmó que el proyecto declara `6000.5.6f1`, pero la primera importación con ese editor sigue siendo la validación autoritativa de símbolos, módulos y serialización.
- Las propiedades Unity 6 `Rigidbody.linearVelocity` y `Rigidbody.linearDamping`, el overload usado de `Physics.RaycastNonAlloc`, `UnityWebRequest.Result` y `Renderer.SetPropertyBlock` existen en la documentación de Unity 6.0. No encontré una discrepancia de firma, pero no pude validar su ensamblado exacto de 6000.5 sin ejecutar/importar el proyecto.
- `UnityEngine.Input` sigue siendo API legado. Su disponibilidad práctica depende de Active Input Handling y de los módulos/plataforma seleccionados; con Input System solamente, touch, mouse, gyro, acelerómetro y `Input.location` no quedan garantizados.
- `Input.location` y `SystemInfo.supportsGyroscope` son sensibles a plataforma y permisos. En WebGL la ruta de GPS está excluida deliberadamente; en móvil falta validar el prompt de permiso y el comportamiento al reanudar la aplicación.
- Escrituras a `Application.persistentDataPath` en WebGL terminan en IndexedDB y su sincronización al cerrar la pestaña no está garantizada por este código. La petición a Open-Meteo también depende de CORS, TLS y conectividad reales del build objetivo.
- `[SerializeReference]` no es una ruta fiable para guardar referencias a `UnityEngine.Object` detrás de una interfaz genérica. `GridManager3D` usa `piecePoolProvider` como ruta concreta, pero el wiring y la restauración de esa referencia deben verificarse en el Inspector.
- `MaterialPropertyBlock` puede modificar color, pero no el estado de renderizado del material. En URP un shader opaco ignorará visualmente el alpha aunque la llamada sea válida.
