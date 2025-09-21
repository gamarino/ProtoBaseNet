# ProtoBaseNet

Una implementación de una base de datos de objetos inmutables.

Este proyecto tiene como objetivo proporcionar una biblioteca para una base de datos de objetos inmutables de alto rendimiento, fácil de usar y segura para subprocesos.

## Estructura del Proyecto

*   `src/ProtoBaseNet`: Contiene el código fuente principal de la biblioteca.
    *   `ImmutableObject.cs`: La clase base abstracta para todos los objetos que se pueden almacenar en la base de datos. Proporciona una propiedad `Id` (GUID) única.
    *   `ProtoDB.cs`: La clase principal de la base de datos. Actualmente, es una implementación en memoria simple con métodos `Put` y `Get`.
*   `tests/ProtoBaseNet.Tests`: Contiene las pruebas unitarias para la biblioteca.
    *   `ProtoDBTests.cs`: Pruebas para la clase `ProtoDB`.
*   `docs`: Contendrá la documentación detallada.
*   `samples`: Contendrá ejemplos de uso.

## Empezando

Actualmente, el proyecto es una biblioteca de clases .NET 8. Para usarlo, debería hacer referencia al proyecto `ProtoBaseNet.csproj` en su propia solución.

## Contribuciones

Las contribuciones son bienvenidas. Por favor, abra un issue para discutir cualquier cambio importante.
