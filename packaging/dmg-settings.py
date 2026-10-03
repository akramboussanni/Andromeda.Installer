import os

application = defines["app"]
files = [application]
symlinks = {"Applications": "/Applications"}
icon = os.path.join(application, "Contents", "Resources", "icon.icns")
format = "UDZO"
background = "builtin-arrow"
window_rect = ((200, 200), (640, 280))
icon_locations = {os.path.basename(application): (140, 120), "Applications": (500, 120)}
default_view = "icon-view"
icon_size = 96
text_size = 14
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
