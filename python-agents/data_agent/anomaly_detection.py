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
        "Unusual transaction pattern detected in the submitted financial report."
        if has_anomaly
        else "No significant transaction anomaly detected in the submitted financial report."
    )

    evidence: list[tuple[str, float, float, str]] = [
        (
            "TransactionAmountZScore",
            transaction_amount_z_score,
            amount_threshold,
            (
                "Transaction amount is significantly above expected range."
                if has_amount_anomaly
                else "Transaction amount is within expected range."
            ),
        ),
        (
            "VelocityScore",
            velocity_score,
            velocity_threshold,
            (
                "Transaction frequency increased abnormally in a short time window."
                if has_velocity_anomaly
                else "Transaction frequency is within expected range."
            ),
        ),
    ]

    return has_anomaly, severity, summary, evidence
