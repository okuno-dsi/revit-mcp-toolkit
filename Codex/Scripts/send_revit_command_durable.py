import os
import runpy

script = os.path.join(os.path.dirname(__file__), "Reference", "send_revit_command_durable.py")
if __name__ == "__main__":
    runpy.run_path(script, run_name="__main__")
