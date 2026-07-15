from __future__ import annotations

from vsmc_research.ethercat import (
    ETHERCAT_CMD_LRW,
    ETHERCAT_ETHERTYPE,
    ETHERCAT_FRAME_TYPE_COMMANDS,
    axis_pdo_from_values,
    build_ethercat_lrw_frame,
    build_process_image,
    expected_lrw_working_counter,
    parse_ethercat_lrw_frame,
)


def _axes():
    return [
        axis_pdo_from_values(
            axis_id="x",
            slave_position=1,
            command_position=0.75,
            actual_position=0.73,
            velocity=0.12,
            following_error=0.02,
            machine_state="move_to_pick",
        ),
        axis_pdo_from_values(
            axis_id="y",
            slave_position=2,
            command_position=0.25,
            actual_position=0.24,
            velocity=0.05,
            following_error=0.01,
            machine_state="move_to_pick",
        ),
    ]


def test_wire_frame_is_ethercat_lrw() -> None:
    axes = _axes()
    process_data = build_process_image(axes, response=True)
    expected_wkc = expected_lrw_working_counter(len(axes))
    frame = build_ethercat_lrw_frame(
        process_data=process_data,
        datagram_index=7,
        working_counter=expected_wkc,
    )
    parsed = parse_ethercat_lrw_frame(frame)
    assert parsed["ether_type"] == ETHERCAT_ETHERTYPE
    assert parsed["frame_type"] == ETHERCAT_FRAME_TYPE_COMMANDS
    assert parsed["command"] == ETHERCAT_CMD_LRW
    assert parsed["command_name"] == "LRW"
    assert parsed["datagram_index"] == 7
    assert parsed["data"] == process_data
    assert parsed["working_counter"] == expected_wkc == 6
    assert len(frame) >= 60


def test_request_and_response_process_images_have_identical_mapping_size() -> None:
    axes = _axes()
    request = build_process_image(axes, response=False)
    response = build_process_image(axes, response=True)
    assert len(request) == len(response) == 56
