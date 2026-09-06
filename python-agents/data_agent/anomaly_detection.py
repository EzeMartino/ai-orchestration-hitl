def analyze_transactions(
    total_amount: float,
    transaction_count: int
) -> tuple[bool, str, str, list[tuple[str, float, float, str]]]:
    average_transaction_amount = (
        total_amount / transaction_count
        if transaction_count > 0
        else 0.0
    )

    transaction_amount_z_score = round(
        average_transaction_amount / 625.0,
        2
    )

    velocity_score = round(
        min(transaction_count / 46.0, 1.0),
        2
    )

    amount_threshold = 3.0
    velocity_threshold = 0.75

    has_amount_anomaly = transaction_amount_z_score > amount_threshold
    has_velocity_anomaly = velocity_score > velocity_threshold

    has_anomaly = has_amount_anomaly or has_velocity_anomaly

    if has_amount_anomaly and has_velocity_anomaly:
        severity = "High"
    elif has_anomaly:
        severity = "Medium"
    else:
        severity = "Low"

    summary = (
        "Se detectó un patrón transaccional inusual en el informe financiero enviado."
        if has_anomaly
        else "No se detectó una anomalía transaccional significativa en el informe financiero enviado."
    )

    evidence: list[tuple[str, float, float, str]] = [
        (
            "TransactionAmountZScore",
            transaction_amount_z_score,
            amount_threshold,
            (
                "La heurística fija de monto promedio por transacción supera el umbral."
                if has_amount_anomaly
                else "La heurística fija de monto promedio por transacción no supera el umbral."
            ),
        ),
        (
            "VelocityScore",
            velocity_score,
            velocity_threshold,
            (
                "La heurística fija de cantidad de transacciones supera el umbral."
                if has_velocity_anomaly
                else "La heurística fija de cantidad de transacciones no supera el umbral."
            ),
        ),
    ]

    return has_anomaly, severity, summary, evidence
