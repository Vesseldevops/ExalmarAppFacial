namespace ExaTareo;

public enum CaptureAngle
{
    Front = 0,
    Left = 1,
    Right = 2,
    Up = 3,
    Down = 4
}

public static class CaptureAngles
{
    public static readonly IReadOnlyList<CaptureAngle> Required =
    [
        CaptureAngle.Front,
        CaptureAngle.Left,
        CaptureAngle.Right,
        CaptureAngle.Up,
        CaptureAngle.Down
    ];

    public static string Label(this CaptureAngle angle) => angle switch
    {
        CaptureAngle.Front => "Frente",
        CaptureAngle.Left => "Gira a la izquierda",
        CaptureAngle.Right => "Gira a la derecha",
        CaptureAngle.Up => "Mira arriba",
        CaptureAngle.Down => "Mira abajo",
        _ => "Captura"
    };

    public static string Guidance(this CaptureAngle angle) => angle switch
    {
        CaptureAngle.Front => "Mira directamente a la cámara.",
        CaptureAngle.Left => "Gira ligeramente el rostro hacia tu izquierda.",
        CaptureAngle.Right => "Gira ligeramente el rostro hacia tu derecha.",
        CaptureAngle.Up => "Levanta un poco el mentón y mira a la cámara.",
        CaptureAngle.Down => "Baja un poco el mentón y mira a la cámara.",
        _ => "Ubica tu rostro dentro del óvalo."
    };
}
